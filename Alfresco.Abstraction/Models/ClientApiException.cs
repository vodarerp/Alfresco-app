namespace Alfresco.Abstraction.Models
{
    /// <summary>
    /// Base exception for Client API operations
    /// </summary>
    public class ClientApiException : Exception
    {
        public int StatusCode { get; }
        public string? ResponseBody { get; }

        public ClientApiException(string message, int statusCode, string? responseBody = null)
            : base($"{message} (Status: {statusCode})")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        public ClientApiException(string message, int statusCode, string? responseBody, Exception? innerException)
            : base($"{message} (Status: {statusCode})", innerException)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }
}
