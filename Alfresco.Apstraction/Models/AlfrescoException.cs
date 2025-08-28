namespace Alfresco.Apstraction.Models
{
    public class AlfrescoException : Exception
    {
        public int StatusCode { get; }
        public string? ResponseBody { get; }

        public AlfrescoException(string message, int statusCode, string responseBody)
            : base($"{message} (Status: {statusCode}) Response: {responseBody}")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }
}
