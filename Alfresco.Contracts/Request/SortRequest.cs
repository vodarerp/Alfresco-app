﻿
namespace Alfresco.Contracts.Request
{
    public class SortRequest
    {
        public string Type { get; set; } = "FIELD";
        public string? Field { get; set; } = "created";
        public bool Ascending { get; set; } = true;

    }
}
