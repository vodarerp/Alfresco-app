
namespace Alfresco.Contracts.Request
{
    public class PagingRequest
    {
        public int MaxItems { get; set; } = 100;
        public int SkipCount { get; set; } = 0;
    }
}
