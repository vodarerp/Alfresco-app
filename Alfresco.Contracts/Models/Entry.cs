using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Models
{
    public class Entry
    {
        public DateTimeOffset CreatedAt { get; set; }
        public bool IsFolder { get; set; }
        public bool IsFile { get; set; }
        public UserInfo CreatedByUser { get; set; } = default!;
        public DateTimeOffset ModifiedAt { get; set; }
        public UserInfo ModifiedByUser { get; set; } = default!;
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string NodeType { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;

        /// <summary>
        /// Raw properties from Alfresco (including custom ecm: properties)
        /// Populated when using include=properties parameter in API calls
        /// </summary>
        public Dictionary<string, object>? Properties { get; set; }

        /// <summary>
        /// Client-specific properties that can be retrieved from Alfresco
        /// or enriched from ClientAPI if not present.
        /// </summary>
        ///
        [JsonPropertyName("properties")]
        public ClientProperties? ClientProperties { get; set; }

        /// <summary>
        /// Path information from Alfresco (populated when using include=path parameter)
        /// Contains the full path to the node including all parent elements
        /// </summary>
        public PathInfo? Path { get; set; }
    }

    /// <summary>
    /// Represents the path to a node in Alfresco
    /// Populated when using include=path in API calls
    /// </summary>
    public class PathInfo
    {
        /// <summary>
        /// Full path name (e.g., "/Company Home/Sites/test/documentLibrary/folder1")
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Whether the path is complete (all elements accessible)
        /// </summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// List of path elements from root to parent folder
        /// The last element is the immediate parent of the node
        /// </summary>
        public List<PathElement>? Elements { get; set; }
    }

    /// <summary>
    /// Represents a single element (folder) in the path hierarchy
    /// </summary>
    public class PathElement
    {
        /// <summary>
        /// Node ID of this path element
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Name of this path element (folder name)
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Node type (typically "cm:folder")
        /// </summary>
        public string? NodeType { get; set; }

        /// <summary>
        /// Aspect names applied to this element
        /// </summary>
        public List<string>? AspectNames { get; set; }
    }
}
