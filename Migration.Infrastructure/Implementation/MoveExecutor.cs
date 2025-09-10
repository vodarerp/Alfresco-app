using Alfresco.Apstraction.Interfaces;
using Migration.Apstaction.Interfaces;
using Oracle.Apstaction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    public class MoveExecutor : IMoveExecutor
    {

        private readonly IDocStagingRepository _docRepo;
        private readonly IAlfrescoWriteApi _write;


        public MoveExecutor(IDocStagingRepository doc, IAlfrescoWriteApi write)
        {
            _docRepo = doc;
            _write = write;
        }


        public Task<int> ExecuteMoveAsync(int take, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
