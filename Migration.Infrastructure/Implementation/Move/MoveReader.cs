using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Models;
using Oracle.Apstaction.Interfaces;
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

            var list = await _doc.TakeReadyForProcessingAsync(take, ct);


            //var toRet = new List<MoveReaderResponse>(list.Count);

            //foreach (var item in list)
            //{
            //    //string DocumentNodeId, string FolderDestId
            //    toRet.Add(new MoveReaderResponse(item.NodeId, item.ToPath));
            //}

            var toRet = list.Select(x => new MoveReaderResponse(x.NodeId, x.ToPath)).ToList() ?? new();

            //return list.Select(x => new MoveReaderResponse
            //{
            //    Id = x.Id,
            //    NodeId = x.NodeId,
            //    ToPath = x.ToPath
            //}).ToList();

            return toRet;


           // throw new NotImplementedException();
        }
    }
}
