using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BtmI2p.GeneralClientInterfaces.CdnProxyServer;
using BtmI2p.JsonRpcHelpers.Client;
using BtmI2p.MiscUtils;
using BtmI2p.Newtonsoft.Json.Linq;
using LinFu.DynamicProxy;
using NLog;

namespace BtmI2p.CdnProxyEndReceiverClientTransport
{
    public class EndReceiverServiceClientInterceptor : IInvokeWrapper
    {
        private readonly IFromClientToCdnProxy _clientToCdn;
        private readonly Guid _endReceiverId;
        private readonly Dictionary<string, RpcMethodInfoWithFees> _rpcMethodInfos;
        public EndReceiverServiceClientInterceptor(
            IFromClientToCdnProxy clientToCdn,
            Guid endReceiverId,
            IEnumerable<RpcMethodInfoWithFees> rpcMethodInfos
            )
        {
            _clientToCdn = clientToCdn;
            _endReceiverId = endReceiverId;
            _rpcMethodInfos = rpcMethodInfos.ToDictionary(
                x => x.JsonRpcMethodInfo.MethodName, 
                x => x
            );
        }

        public void BeforeInvoke(InvocationInfo info)
        {
        }

        public object DoInvoke(InvocationInfo info)
        {
            return JsonRpcClientProcessor.DoInvokeHelper(info, DoInvokeImpl);
        }

        private readonly static Logger _logger = LogManager.GetCurrentClassLogger();
        private async Task<object> DoInvokeImpl(InvocationInfo info)
        {
            var jsonRequest = JsonRpcClientProcessor.GetJsonRpcRequest(info);
            var jsonRequestData = Encoding.UTF8.GetBytes(jsonRequest.ToString());
            var processedDataResult = 
                await _clientToCdn.ProcessPacketCheckVersion(
                    _endReceiverId, 
                    jsonRequestData,
                    new VersionCompatibilityRequest()
                ).ConfigureAwait(false);
            return await JsonRpcClientProcessor.GetJsonRpcResult(
                JObject.Parse(
                    Encoding.UTF8.GetString(
                        processedDataResult.BytesResult
                    )
                ), 
                info
            ).ConfigureAwait(false);
        }

        public void AfterInvoke(InvocationInfo info, object returnValue)
        {
        }

        public static async Task<T1> GetClientProxy<T1>(
            IFromClientToCdnProxy clientToCdn,
            Guid endReceiverId,
            CancellationToken cancelationToken,
            List<RpcMethodInfoWithFees> endReceiverMethodInfos = null
        )
        {
            if (endReceiverMethodInfos == null)
            {
                while (true)
                {
                    try
                    {
                        endReceiverMethodInfos =
                            await clientToCdn.GetEndReceiverFeesCheckVersion(
                                endReceiverId, new VersionCompatibilityRequest()
                            ).ThrowIfCancelled(cancelationToken).ConfigureAwait(false);
                        break;
                    }
                    catch (TimeoutException)
                    {
                    }
                }
            }
            if (
                !JsonRpcClientProcessor.CheckRpcServerMethodInfos(
                    typeof (T1),
                    endReceiverMethodInfos
                        .Select(x => x.JsonRpcMethodInfo)
                        .ToList()
                )
            )
            {
                throw new Exception(
                    string.Format(
                        "End receiver method infos not matches with T1"
                    )
                );
            }
            var factory = new ProxyFactory();
            var interceptor = new EndReceiverServiceClientInterceptor(
                clientToCdn,
                endReceiverId,
                endReceiverMethodInfos
            );
            return factory.CreateProxy<T1>(interceptor);
        }
    }
}
