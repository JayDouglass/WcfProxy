extern alias CastleDll;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;
using ProxyGenerator = CastleDll.Castle.DynamicProxy.ProxyGenerator;

namespace WcfClientIsolator.Tests
{
    public class WcfProxyFactory
    {
        private readonly ProxyGenerator generator = new ProxyGenerator();

        public T Create<T>(Func<object> createChannel)
        {
            var channelManager = new WcfChannelManager(createChannel);
            var interceptor = new WcfInterceptor(channelManager);
            var interfaces = new Type[] { typeof(T), typeof(IClientChannel) };
            var proxy = generator.CreateInterfaceProxyWithTarget(typeof(IWcfChannelManager), interfaces, channelManager, interceptor);
            return (T)proxy;
        }
    }
}
