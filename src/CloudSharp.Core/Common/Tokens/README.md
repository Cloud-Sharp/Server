# Tokens

보안 토큰 발급 port와 결과 타입. 평문은 32바이트 CSPRNG 난수를 base64url(padding 없음)로
인코딩하고 종류별 고정 prefix를 붙인다. 저장소에는 HMAC-SHA-256 해시만 보관하며 평문은
발급 시 한 번만 호출부에 반환한다. 구현체와 비밀키 주입은 `CloudSharp.Infrastructure.Auth.Tokens`에 있다.