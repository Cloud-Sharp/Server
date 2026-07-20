using CloudSharp.Core.Common.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CloudSharp.Infrastructure.Auth.Tokens;

/// <summary>
/// 토큰 생성기/해시기/발급기 DI 등록 확장 메서드. 모두 singleton으로 등록하고
/// <see cref="TokenHashingOptionsValidator"/>로 시작 시점 fail-fast를 보장한다.
/// </summary>
public static class TokenServiceCollectionExtensions
{
    /// <summary>
    /// <c>TokenHashing</c> 설정 바인딩과 <see cref="ITokenGenerator"/>, <see cref="ITokenHasher"/>,
    /// <see cref="ITokenIssuer"/> singleton 등록을 한 번에 수행한다. 등록 시점에 활성키를
    /// 즉시 검증해 유효하지 않으면 예외를 던져 호스트 시작을 중단한다.
    /// </summary>
    public static IServiceCollection AddTokenHashing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TokenHashingOptions>()
            .Bind(configuration.GetSection(TokenHashingOptions.SectionName));
        services.AddSingleton<IValidateOptions<TokenHashingOptions>, TokenHashingOptionsValidator>();

        var options = configuration.GetSection(TokenHashingOptions.SectionName).Get<TokenHashingOptions>()
            ?? new TokenHashingOptions();
        var validationResult = new TokenHashingOptionsValidator().Validate(null, options);
        if (validationResult.Failed)
        {
            throw new InvalidOperationException(validationResult.FailureMessage);
        }

        services.AddSingleton<ITokenGenerator, RandomTokenGenerator>();
        services.AddSingleton<ITokenHasher>(sp =>
            new HmacSha256TokenHasher(sp.GetRequiredService<IOptions<TokenHashingOptions>>().Value));
        services.AddSingleton<ITokenIssuer, TokenIssuer>();

        return services;
    }
}