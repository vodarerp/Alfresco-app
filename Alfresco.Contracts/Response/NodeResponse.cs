using Alfresco.Contracts.Models;

namespace Alfresco.Contracts.Response
{
    /// <summary>
    /// Response containing a single node entry
    /// </summary>
    public class NodeResponse
    {
        public Entry Entry { get; set; } = default!;
    }
}
