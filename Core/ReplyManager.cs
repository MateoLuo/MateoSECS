using MateoSECS.Exceptions;
using MateoSECS.Interface;
using MateoSECS.Secs;
using System;
using System.Collections.Generic;
using System.Text;

namespace MateoSECS.Core
{
    internal class ReplyManager
    {
        public ReplyManager(ISecsLogger logger)
        {
            
        }

        internal PendingRequest Register(SecsMsg msg, uint systemBytes, TimeSpan t3)
        {
            throw new NotImplementedException();
        }

        internal bool TryComplete(SecsMsg secs, uint systemBytes)
        {
            throw new NotImplementedException();
        }

        internal void TryFail(uint systemBytes, SecsCommunicationException secsCommunicationException)
        {
            throw new NotImplementedException();
        }
        public bool TryFail(uint systemBytes, Exception error)
        {
            throw new NotImplementedException();
        }

        internal void Dispose()
        {
            throw new NotImplementedException();
        }

        internal void FailAll(Exception error)
        {
            throw new NotImplementedException();
        }
    }
}
