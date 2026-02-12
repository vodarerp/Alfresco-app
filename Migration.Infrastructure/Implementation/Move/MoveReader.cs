using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Move
{
    public class MoveReader : IMoveReader
    {
        private readonly IDocStagingRepository _doc;

        public MoveReader(IDocStagingRepository doc)
        {
            _doc = doc;
        }
        public async Task<IReadOnlyList<MoveReaderResponse>> ReadBatchAsync(int take, CancellationToken ct)
        {

            var list = await _doc.TakeReadyForProcessingAsync(take, ct).ConfigureAwait(false);

            var toRet = list.Select(x => new MoveReaderResponse(x.Id,x.NodeId, x.ToPath)).ToList() ?? new();

            

            return toRet;


          
        }
    }
}
