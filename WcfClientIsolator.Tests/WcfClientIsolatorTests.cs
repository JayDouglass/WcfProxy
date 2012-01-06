extern alias CastleDll;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Moq;
using System.ServiceModel;
using ProxyGenerator = CastleDll.Castle.DynamicProxy.ProxyGenerator;
using IInterceptor = CastleDll.Castle.DynamicProxy.IInterceptor;
using ProxyGenerationOptions = CastleDll.Castle.DynamicProxy.ProxyGenerationOptions;
using IInvocation = CastleDll.Castle.DynamicProxy.IInvocation;
using IProxyTargetAccessor = CastleDll.Castle.DynamicProxy.IProxyTargetAccessor;
using System.ServiceModel.Channels;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Reflection;

namespace WcfClientIsolator.Tests
{
    public class WcfClientIsolatorTests : IDisposable
    {
        #region "Setup & Teardown"
        Uri uri = new Uri("net.tcp://localhost:8000/HelloWorld");
        ServiceHost service;

        public WcfClientIsolatorTests()
        {
            service = new ServiceHost(typeof(HelloWorldService), uri);
            var binding = new NetTcpBinding();
            service.AddServiceEndpoint(typeof(IHelloWorldService), binding, uri);
            service.Open();
        }

        public void Dispose()
        {
            service.Close();
        }
        #endregion

        [Fact]
        public void wcf_channel_throws_communication_object_faulted_exception_if_disposed_while_faulted()
        {
            var channel = ChannelFactory<IHelloWorldService>
                .CreateChannel(new NetTcpBinding(), new EndpointAddress(uri));

            Assert.Throws<CommunicationObjectFaultedException>(delegate
            {
                using ((IClientChannel)channel)
                {
                    channel.ThrowException();
                }
            });
        }

        [Fact]
        public void wcf_proxy_does_not_throw_communication_object_faulted_exception_when_faulted_channel_disposed()
        {
            // Arrange
            Func<object> createChannel = () =>
                ChannelFactory<IHelloWorldService>
                    .CreateChannel(new NetTcpBinding(), new EndpointAddress(uri));
            var factory = new WcfProxyFactory();
            var proxy = factory.Create<IDisposableHelloWorldService>(createChannel);

            Assert.Throws<FaultException>(delegate
            {
                using (proxy)
                {
                    proxy.ThrowException();
                }
            });
        }

        [Fact]
        public void wcf_proxy_fowards_calls_to_wcf_channel()
        {
            // Arrange
            Func<object> createChannel = () =>
                ChannelFactory<IHelloWorldService>
                    .CreateChannel(new NetTcpBinding(), new EndpointAddress(uri));
            var factory = new WcfProxyFactory();
            var proxy = factory.Create<IDisposableHelloWorldService>(createChannel);

            // Act
            var result = proxy.HelloWorld();

            // Assert
            Assert.Equal("Hello World!", result);
        }

        [Fact]
        public void wcf_proxy_refreshes_faulted_channel_before_invoking()
        {
            // Arrange
            Func<object> createChannel = () =>
                ChannelFactory<IHelloWorldService>
                    .CreateChannel(new NetTcpBinding(), new EndpointAddress(uri));
            var factory = new WcfProxyFactory();
            var proxy = factory.Create<IDisposableHelloWorldService>(createChannel);

            // Act
            try
            {
                proxy.ThrowException();
            }
            catch
            {
            }

            // Assert
            var accessor = proxy as IProxyTargetAccessor;
            var manager = accessor.DynProxyGetTarget() as IWcfChannelManager;

            Assert.Equal(CommunicationState.Faulted, manager.Channel.State);
            Assert.DoesNotThrow(delegate
            {
                proxy.HelloWorld();
            });
        }

        [Fact]
        public void wcf_proxy_disposes_faulted_channel_without_throwing()
        {
            // Arrange
            Func<object> createChannel = () =>
                ChannelFactory<IHelloWorldService>
                    .CreateChannel(new NetTcpBinding(), new EndpointAddress(uri));
            var factory = new WcfProxyFactory();
            var proxy = factory.Create<IDisposableHelloWorldService>(createChannel);

            // Act
            try
            {
                proxy.ThrowException();
            }
            catch
            {
            }

            // Assert
            Assert.DoesNotThrow(delegate
            {
                proxy.Dispose();
            });
        }

        [Fact]
        public void can_inject_mock_service_implementation_into_service_host()
        {
            // Arrange
            Castle.DynamicProxy.Generators.
                AttributesToAvoidReplicating.Add(typeof(ServiceContractAttribute));
            var helloWorldService = new Mock<IHelloWorldService>();
            helloWorldService.Setup(s => s.HelloWorld()).Returns("mocked result");

            var uri = new Uri("net.tcp://localhost:8001/HelloWorld");
            var service = new ServiceHost(helloWorldService.Object, uri);
            var binding = new NetTcpBinding();
            service.AddServiceEndpoint(typeof(IHelloWorldService), binding, uri);
            service.Description.Behaviors.Find<ServiceBehaviorAttribute>()
                   .InstanceContextMode = InstanceContextMode.Single;
            service.Open();

            var client = ChannelFactory<IHelloWorldService>.CreateChannel(new NetTcpBinding(), new EndpointAddress(uri));

            // Act
            var result = client.HelloWorld();
            service.Close();

            Assert.Equal("mocked result", result);
        }
    }

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
