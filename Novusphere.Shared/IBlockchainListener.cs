using System;
using MongoDB.Driver;

namespace Novusphere.Shared
{
    public interface IBlockchainListener
    {
        void Start(IMongoDatabase db);
        void Process(IMongoDatabase db);
    }
}
