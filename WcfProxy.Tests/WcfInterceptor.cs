extern alias CastleDll;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IInterceptor = CastleDll.Castle.DynamicProxy.IInterceptor;
using IInvocation = CastleDll.Castle.DynamicProxy.IInvocation;

namespace WcfClientIsolator.Tests
{
    public class WcfInterceptor : IInterceptor
    {
        WcfChannelManager channelManager;

        public WcfInterceptor(WcfChannelManager channelManager)
        {
            this.channelManager = channelManager;
        }

        public void Intercept(IInvocation invocation)
        {
            if (invocation.Method.DeclaringType.Name == "IDisposable"
                && invocation.Method.Name == "Dispose")
                channelManager.Dispose();

            channelManager.Invoke(invocation);
        }
    }
}
