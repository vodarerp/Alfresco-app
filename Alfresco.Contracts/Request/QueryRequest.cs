
using System.Text.Json.Serialization;
namespace Alfresco.Contracts.Request
{
    public class QueryRequest
    {
        public string Language { get; set; } = "afts";

        
        public string Query { get; set; } = "created:['2011-02-15T00:00:00' TO '2012-02-15T00:00:00']";
    }
}
