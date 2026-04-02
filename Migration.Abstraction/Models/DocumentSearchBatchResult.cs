using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Abstraction.Models
{
    public class DocumentSearchBatchResult
    {       
        public int DocumentsFound { get; set; }
        public int DocumentsInserted { get; set; }
        public int FoldersFound { get; set; }
        public int FoldersInserted { get; set; }
        public bool HasMore { get; set; }
        public List<string> Errors { get; set; } = new();
        public DocumentSearchBatchResult() { }

        public DocumentSearchBatchResult(int documentsFound, int documentsInserted, int foldersFound, int foldersInserted, bool hasMore = false)
        {
            DocumentsFound = documentsFound;
            DocumentsInserted = documentsInserted;
            FoldersFound = foldersFound;
            FoldersInserted = foldersInserted;
            HasMore = hasMore;
        }
    }
}
