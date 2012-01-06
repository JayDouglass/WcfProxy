using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;

namespace WcfClientIsolator.Tests
{
    [ServiceContract]
    public interface IHelloWorldService
    {
        [OperationContract]
        string HelloWorld();

        [OperationContract]
        void ThrowException();
    }

    public class HelloWorldService : IHelloWorldService
    {
        public string HelloWorld()
        {
            return "Hello World!";
        }


        public void ThrowException()
        {
            throw new Exception();
        }
    }

    public interface IDisposableHelloWorldService : IHelloWorldService, IDisposable
    {
    }
}
