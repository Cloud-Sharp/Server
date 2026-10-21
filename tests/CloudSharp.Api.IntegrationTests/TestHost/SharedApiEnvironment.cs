namespace CloudSharp.Api.IntegrationTests.TestHost;

internal static class SharedApiEnvironment
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static ApiIntegrationEnvironment? _environment;

    internal static async Task<ApiIntegrationEnvironment> StartAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _environment ??= await ApiIntegrationEnvironment.StartAsync();
            return _environment;
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static async Task StopAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_environment is not null)
            {
                await _environment.DisposeAsync();
                _environment = null;
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
