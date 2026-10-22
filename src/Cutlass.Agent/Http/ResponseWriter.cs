using System.Net;
using System.Text;
using Cutlass.Agent.Api;

namespace Cutlass.Agent.Http;

/// <summary>Writes an <see cref="ApiResult"/> as the HTTP response (AGENT_INTEGRATION 4.2 and 4.3).</summary>
public static class ResponseWriter
{
    /// <summary>Writes the result and closes the response.</summary>
    /// <param name="response">The response.</param>
    /// <param name="result">The result.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when written.</returns>
    public static async Task WriteAsync(HttpListenerResponse response, ApiResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            response.StatusCode = result.HttpStatus;
            response.ContentType = result.ContentType;
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Cutlass-Revision"] = result.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (result.Error?.RetryAfterMs is { } retry)
            {
                response.Headers["Retry-After"] = Math.Max(1, (retry + 999) / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (result.Headers is { } headers && result.Error is null)
            {
                foreach (var (key, value) in headers)
                {
                    response.Headers[key] = value;
                }
            }

            var body = Body(result);
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The client went away.
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Already closed.
            }
        }
    }

    /// <summary>The body bytes of a result: the PNG, the text, or the JSON envelope.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Body(ApiResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Error is null && result.Png is { } png)
        {
            return png;
        }

        if (result.Error is null && result.Text is { } text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        return Encoding.UTF8.GetBytes(result.ToJson());
    }
}
