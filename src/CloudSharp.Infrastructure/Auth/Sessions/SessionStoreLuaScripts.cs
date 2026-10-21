namespace CloudSharp.Infrastructure.Auth.Sessions;

/// <summary>
/// 세션 저장·삭제·비밀번호 변경 후처리용 Lua script. 본문 key와 사용자 인덱스 key를
/// 하나의 script로 원자적으로 변경한다. 단일 Redis 인스턴스를 전제로 하므로 script 안에서
/// 인덱스 member로 본문 key를 구성한다(인덱스 member는 token hash, 본문 key는 접두사 + token hash).
/// 식별자·보안 버전·시각은 문자열 그대로 기록하고 Lua 숫자 변환은 TTL·만료 비교에만 사용해
/// 값의 훼손을 방지한다. TTL은 Redis 서버 시각 기준으로 absolute 만료를 넘지 않게 계산한다.
/// </summary>
internal static class SessionStoreLuaScripts
{
    /// <summary>사용자당 최대 활성 세션 수.</summary>
    internal const int MaxSessions = 10;

    /// <summary>
    /// 세션 저장. KEYS[1]=본문 key, KEYS[2]=사용자 인덱스 key.
    /// ARGV[1..8]=본문 필드값(sessionId, userId, userPublicId, securityVersion, issuedAt, lastSeenAt,
    /// idleExpiresAt, absoluteExpiresAt), ARGV[9]=token hash member, ARGV[10]=issuedAt Unix seconds(score),
    /// ARGV[11]=최대 세션 수, ARGV[12]=본문 key 접두사.
    /// 인덱스의 유령(member만 존재)·만료 세션을 정리한 뒤 본문을 저장하고 인덱스에 추가하며,
    /// 초과분을 issuedAt, 동시각이면 token hash 사전순으로 폐기한다. 이미 본문이 있으면 -1을 반환해
    /// 덮어쓰기를 거부한다. 본문 TTL은 absolute 만료를 넘지 않고, 인덱스는 남은 세션의
    /// 최종 absolute 만료까지만 유지하며 비면 삭제한다.
    /// </summary>
    internal const string Store = """
        local bodyKey = KEYS[1]
        local indexKey = KEYS[2]
        local member = ARGV[9]
        local bodyPrefix = ARGV[12]
        local maxSessions = tonumber(ARGV[11])
        local time = redis.call('TIME')
        local nowTicks = tonumber(time[1]) * 10000000 + tonumber(time[2]) * 10 + 621355968000000000 -- Unix epoch in .NET ticks

        local members = redis.call('ZRANGE', indexKey, 0, -1)
        for _, m in ipairs(members) do
            local bodyKeyOfMember = bodyPrefix .. m
            if redis.call('EXISTS', bodyKeyOfMember) == 0 then
                redis.call('ZREM', indexKey, m)
            else
                local idle = redis.call('HGET', bodyKeyOfMember, 'idleExpiresAt')
                local absolute = redis.call('HGET', bodyKeyOfMember, 'absoluteExpiresAt')
                local idleTicks = tonumber(idle)
                local absoluteTicks = tonumber(absolute)
                if idleTicks == nil or absoluteTicks == nil
                    or nowTicks >= idleTicks or nowTicks >= absoluteTicks then
                    redis.call('DEL', bodyKeyOfMember)
                    redis.call('ZREM', indexKey, m)
                end
            end
        end

        if redis.call('EXISTS', bodyKey) == 1 then
            return -1
        end

        redis.call('HSET', bodyKey,
            'sessionId', ARGV[1],
            'userId', ARGV[2],
            'userPublicId', ARGV[3],
            'securityVersion', ARGV[4],
            'issuedAt', ARGV[5],
            'lastSeenAt', ARGV[6],
            'idleExpiresAt', ARGV[7],
            'absoluteExpiresAt', ARGV[8])
        local bodyTtlMs = math.floor((tonumber(ARGV[8]) - nowTicks) / 10000)
        if bodyTtlMs < 1 then
            bodyTtlMs = 1
        end
        redis.call('PEXPIRE', bodyKey, bodyTtlMs)

        redis.call('ZADD', indexKey, tonumber(ARGV[10]), member)

        -- Scores retain their existing Unix seconds format. Eviction uses exact
        -- fixed-width ticks, so different times within one second are not ties.
        local remaining = {}
        for _, m in ipairs(redis.call('ZRANGE', indexKey, 0, -1)) do
            local fields = redis.call('HMGET', bodyPrefix .. m, 'issuedAt', 'absoluteExpiresAt')
            if fields[1] == false or fields[2] == false then
                return redis.error_reply('Stored session ordering fields are missing.')
            end
            table.insert(remaining, { member = m, issuedAt = fields[1], absolute = tonumber(fields[2]) })
        end
        table.sort(remaining, function(left, right)
            if left.issuedAt == right.issuedAt then
                return left.member < right.member
            end
            return left.issuedAt < right.issuedAt
        end)

        local evicted = math.max(0, #remaining - maxSessions)
        local maxAbsolute = 0
        for i, session in ipairs(remaining) do
            if i <= evicted then
                redis.call('DEL', bodyPrefix .. session.member)
                redis.call('ZREM', indexKey, session.member)
            elseif session.absolute ~= nil and session.absolute > maxAbsolute then
                maxAbsolute = session.absolute
            end
        end

        if #remaining - evicted == 0 then
            redis.call('DEL', indexKey)
        else
            local indexTtlMs = math.floor((maxAbsolute - nowTicks) / 10000)
            if indexTtlMs < 1 then
                indexTtlMs = 1
            end
            redis.call('PEXPIRE', indexKey, indexTtlMs)
        end
        return 1
        """;

    /// <summary>
    /// 세션 삭제. KEYS[1]=본문 key, KEYS[2]=사용자 인덱스 key.
    /// ARGV[1]=token hash member, ARGV[2]=본문 key 접두사.
    /// 본문과 인덱스 member를 삭제하고 이미 삭제된 세션이면 그대로 성공한다.
    /// 인덱스가 비면 삭제하고, 남으면 남은 세션의 최종 absolute 만료까지만 유지한다.
    /// </summary>
    internal const string Remove = """
        local bodyKey = KEYS[1]
        local indexKey = KEYS[2]
        local member = ARGV[1]
        local bodyPrefix = ARGV[2]

        redis.call('DEL', bodyKey)
        redis.call('ZREM', indexKey, member)

        local members = redis.call('ZRANGE', indexKey, 0, -1)
        if #members == 0 then
            redis.call('DEL', indexKey)
            return 0
        end

        local time = redis.call('TIME')
        local nowTicks = tonumber(time[1]) * 10000000 + tonumber(time[2]) * 10 + 621355968000000000 -- Unix epoch in .NET ticks

        local maxAbsolute = 0
        for _, m in ipairs(members) do
            local bodyKeyOfMember = bodyPrefix .. m
            if redis.call('EXISTS', bodyKeyOfMember) == 0 then
                redis.call('ZREM', indexKey, m)
            else
                local absolute = redis.call('HGET', bodyKeyOfMember, 'absoluteExpiresAt')
                local absoluteTicks = absolute == false and nil or tonumber(absolute)
                if absoluteTicks == nil then
                    redis.call('ZREM', indexKey, m)
                elseif absoluteTicks > maxAbsolute then
                    maxAbsolute = absoluteTicks
                end
            end
        end

        if redis.call('ZCARD', indexKey) == 0 then
            redis.call('DEL', indexKey)
            return 0
        end

        local indexTtlMs = math.floor((maxAbsolute - nowTicks) / 10000)
        if indexTtlMs < 1 then
            indexTtlMs = 1
        end
        redis.call('PEXPIRE', indexKey, indexTtlMs)
        return 0
        """;

    /// <summary>
    /// 비밀번호 변경 commit 후처리. KEYS[1]=현재 세션 본문 key, KEYS[2]=사용자 인덱스 key.
    /// ARGV[1]=기대 sessionId, ARGV[2]=기대 userId, ARGV[3]=변경 전 보안 버전, ARGV[4]=새 보안 버전,
    /// ARGV[5]=현재 시각 ticks, ARGV[6]=token hash member, ARGV[7]=본문 key 접두사.
    /// 저장된 현재 세션이 소유자·세션 ID·변경 전 보안 버전·유효 만료와 일치하고 새 보안 버전이 후퇴하지
    /// 않으면 본문의 보안 버전만 교체한다(시각·본문 TTL은 보존). 어떤 경우든 현재 token을 제외한
    /// 같은 사용자의 세션을 모두 삭제하고, 적용 여부를 1/0으로 반환한다. 현재 세션이 이미
    /// 삭제·만료·변경되었으면 본문을 되살리거나 되돌리지 않는다.
    /// </summary>
    internal const string FinalizePasswordChange = """
        local bodyKey = KEYS[1]
        local indexKey = KEYS[2]
        local member = ARGV[6]
        local bodyPrefix = ARGV[7]

        local eligible = false
        if redis.call('EXISTS', bodyKey) == 1 then
            local storedSessionId = redis.call('HGET', bodyKey, 'sessionId')
            local storedUserId = redis.call('HGET', bodyKey, 'userId')
            local storedVersion = redis.call('HGET', bodyKey, 'securityVersion')
            local storedIdle = redis.call('HGET', bodyKey, 'idleExpiresAt')
            local storedAbsolute = redis.call('HGET', bodyKey, 'absoluteExpiresAt')
            if storedSessionId == ARGV[1] and storedUserId == ARGV[2] and storedVersion == ARGV[3]
                and storedIdle ~= false and storedAbsolute ~= false then
                local nowTicks = tonumber(ARGV[5])
                local idleTicks = tonumber(storedIdle)
                local absoluteTicks = tonumber(storedAbsolute)
                local newVersion = ARGV[4]
                -- Versions are canonical positive decimal strings, not Lua doubles.
                local versionDoesNotRegress = string.match(newVersion, '^[1-9][0-9]*$') ~= nil
                    and (#newVersion > #storedVersion
                        or (#newVersion == #storedVersion and newVersion >= storedVersion))
                if idleTicks ~= nil and absoluteTicks ~= nil
                    and nowTicks < idleTicks and nowTicks < absoluteTicks
                    and versionDoesNotRegress then
                    eligible = true
                end
            end
        end

        local members = redis.call('ZRANGE', indexKey, 0, -1)
        for _, m in ipairs(members) do
            if m ~= member then
                redis.call('DEL', bodyPrefix .. m)
                redis.call('ZREM', indexKey, m)
            end
        end

        if eligible then
            redis.call('HSET', bodyKey, 'securityVersion', ARGV[4])
        end

        local remaining = redis.call('ZRANGE', indexKey, 0, -1)
        if #remaining == 0 then
            redis.call('DEL', indexKey)
        else
            local time = redis.call('TIME')
            local nowTicks = tonumber(time[1]) * 10000000 + tonumber(time[2]) * 10 + 621355968000000000 -- Unix epoch in .NET ticks
            local maxAbsolute = 0
            local liveMembers = 0
            for _, m in ipairs(remaining) do
                local bodyKeyOfMember = bodyPrefix .. m
                if redis.call('EXISTS', bodyKeyOfMember) == 1 then
                    local absolute = redis.call('HGET', bodyKeyOfMember, 'absoluteExpiresAt')
                    local absoluteTicks = absolute == false and nil or tonumber(absolute)
                    if absoluteTicks ~= nil then
                        liveMembers = liveMembers + 1
                        if absoluteTicks > maxAbsolute then
                            maxAbsolute = absoluteTicks
                        end
                    end
                end
            end
            if liveMembers == 0 then
                redis.call('DEL', indexKey)
            else
                local indexTtlMs = math.floor((maxAbsolute - nowTicks) / 10000)
                if indexTtlMs < 1 then
                    indexTtlMs = 1
                end
                redis.call('PEXPIRE', indexKey, indexTtlMs)
            end
        end

        if eligible then
            return 1
        end
        return 0
        """;
}
