extern alias CastleDll;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel.Channels;
using IInvocation = CastleDll.Castle.DynamicProxy.IInvocation;
using System.Reflection;
using System.ServiceModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace WcfClientIsolator.Tests
{
    public interface IWcfChannelManager
    {
        IChannel Channel { get; }
    }

    public class WcfChannelManager : IWcfChannelManager
    {
        readonly Func<object> createChannel;
        public IChannel Channel { get; private set; }
        int disposed;

        public WcfChannelManager(Func<object> createChannel)
        {
            this.createChannel = createChannel;
        }

        public void Invoke(IInvocation invocation)
        {
            RefreshChannel();

            try
            {
                var result = invocation.Method.Invoke(Channel, invocation.Arguments);
                invocation.ReturnValue = result;
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }

        public bool IsChannelUsable
        {
            get
            {
                switch (Channel.State)
                {
                    case CommunicationState.Closed:
                    case CommunicationState.Closing:
                    case CommunicationState.Faulted:
                        return false;
                }
                return true;
            }
        }

        private void CreateChannel()
        {
            Channel = (IChannel)createChannel();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void RefreshChannel()
        {
            if (disposed == 0 && (Channel == null || IsChannelUsable == false))
            {
                if (Channel != null)
                {
                    ReleaseChannel();
                }

                CreateChannel();
            }
        }

        private void ReleaseChannel()
        {
            bool success = false;
            try
            {
                if (Channel.State != CommunicationState.Faulted)
                {
                    Channel.Close();
                    success = true;
                }
            }
            finally
            {
                if (!success)
                {
                    Channel.Abort();
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref disposed, 1, 0) == 1)
                return;

            if (Channel != null)
                ReleaseChannel();
        }
    }
}
