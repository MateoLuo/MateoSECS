using MateoSECS.Secs;

namespace MateoSECS.Core
{
    public sealed class PendingRequest : IDisposable
    {
        public void Dispose()
        {
            throw new NotImplementedException();
        }

        internal async Task<SecsMsg> WaitAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
