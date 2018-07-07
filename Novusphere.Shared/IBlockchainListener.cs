using System;
using MongoDB.Driver;

namespace Novusphere.Shared
{
    public interface IBlockchainListener
    {
        void Start(NovusphereConfig config, IMongoDatabase db);
        void Process(IMongoDatabase db);
    }
}
