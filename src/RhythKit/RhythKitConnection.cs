using System.Diagnostics;

namespace RhythKit;

public sealed class RhythKitConnection(RhythiansClient client)
{
    public async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var authorization = await client.StartDeviceAuthorizationAsync(cancellationToken);
        Process.Start(new ProcessStartInfo { FileName = authorization.VerificationUrl, UseShellExecute = true });

        var deadline = DateTime.UtcNow.AddSeconds(authorization.ExpiresIn);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await client.PollDeviceAuthorizationAsync(authorization.DeviceCode, cancellationToken);
            if (result?.Status == "authorized" && !string.IsNullOrWhiteSpace(result.Token)) return result.Token;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException("Rhythians authorization expired.");
    }
}
