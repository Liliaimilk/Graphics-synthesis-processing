using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public partial class Form1
    {
        /// <summary>
        /// 主窗体显示后启动远程 WebSocket 客户端。
        /// </summary>
        private async Task OnMainFormShownAsync()
        {
            await StartRemoteWebSocketServerCoreAsync();
        }

        /// <summary>
        /// 主窗体关闭时释放远程连接相关资源。
        /// </summary>
        private void HandleFormClosingCleanup()
        {
            StopRemoteWebSocketServerCore();
            activeRemoteDialog?.Close();
        }

        /// <summary>
        /// 在未运行时启动远程 WebSocket 处理循环。
        /// </summary>
        private async Task StartRemoteWebSocketServerCoreAsync()
        {
            if (remoteWebSocketReceiveTask != null && !remoteWebSocketReceiveTask.IsCompleted)
            {
                return;
            }

            try
            {
                remoteWebSocketCts?.Dispose();
                remoteWebSocketCts = new CancellationTokenSource();
                remoteWebSocketReceiveTask = Task.Run(() => RunRemoteWebSocketClientLoopCoreAsync(remoteWebSocketCts.Token));
                lblStatus.Text = "正在连接远程...";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "服务启动失败";
                MessageBox.Show($"无法启动 WS 客户端: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                StopRemoteWebSocketServerCore();
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 停止远程 WebSocket 客户端并清理相关状态。
        /// </summary>
        private void StopRemoteWebSocketServerCore()
        {
            try
            {
                remoteWebSocketCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                if (remoteWebSocketClient != null)
                {
                    if (remoteWebSocketClient.State == WebSocketState.Open || remoteWebSocketClient.State == WebSocketState.CloseReceived)
                    {
                        remoteWebSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None).GetAwaiter().GetResult();
                    }

                    remoteWebSocketClient.Dispose();
                }
            }
            catch
            {
            }

            remoteWebSocketClient = null;
            remoteWebSocketReceiveTask = null;
            remoteWebSocketCts?.Dispose();
            remoteWebSocketCts = null;
        }

        /// <summary>
        /// 维持一个可重连的远程客户端循环，直到收到取消信号。
        /// </summary>
        private async Task RunRemoteWebSocketClientLoopCoreAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ClientWebSocket client = null;
                try
                {
                    client = new ClientWebSocket();
                    remoteWebSocketClient = client;

                    UpdateStatusSafely($"正在连接");// : {RemoteWebSocketEndpoint}
                    await client.ConnectAsync(new Uri(RemoteWebSocketEndpoint), cancellationToken);
                    UpdateStatusSafely("远程已连接");

                    await ReceiveRemoteMessagesCoreAsync(client, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    UpdateStatusSafely($"连接失败，稍后重试: {ex.Message}");
                }
                catch (Exception ex)
                {
                    UpdateStatusSafely($"运行异常，稍后重试: {ex.Message}");
                }
                finally
                {
                    if (remoteWebSocketClient == client)
                    {
                        remoteWebSocketClient = null;
                    }

                    try
                    {
                        client?.Dispose();
                    }
                    catch
                    {
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 逐条接收并解析远程 WebSocket 消息。
        /// </summary>
        private async Task ReceiveRemoteMessagesCoreAsync(WebSocket webSocket, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            StringBuilder builder = new StringBuilder();

            while (webSocket != null && webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                builder.Clear();
                WebSocketReceiveResult result;

                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                        }

                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                }
                while (!result.EndOfMessage);

                if (builder.Length == 0)
                {
                    continue;
                }

                try
                {
                    RemoteMergeRequest request = BuildRemoteMergeRequest(builder.ToString());
                    BeginInvoke(new Action(() => QueueRemoteMergeRequest(request)));
                }
                catch (Exception ex)
                {
                    UpdateStatusSafely($"远程消息解析失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 将原始 JSON 转换为可执行的远程套图请求。
        /// </summary>
        private RemoteMergeRequest BuildRemoteMergeRequest(string rawJson)
        {
            RemoteMergeMessage message = DeserializeRemoteMessage(rawJson);
            var settings = Properties.Settings.Default;

            List<string> materialNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(message?.MaterialName))
            {
                materialNames.Add(message.MaterialName.Trim());
            }

            if (message?.MaterialNames != null)
            {
                materialNames.AddRange(
                    message.MaterialNames
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name.Trim()));
            }

            materialNames = materialNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (materialNames.Count == 0)
            {
                throw new InvalidOperationException("消息中未提供素材名称");
            }

            return new RemoteMergeRequest
            {
                TemplateFolder = settings.TemplateFolder,
                MaterialFolder = settings.MaterialFolder,
                SavePath = settings.SavePath,
                MaterialNames = materialNames,
                TemplateName = message?.TemplateName,
                Format = message?.Format,
                CompositeMode = message?.CompositeMode,
                WhiteInk = message?.WhiteInk,
                Varnish = message?.Varnish,
                Separator = message?.Separator,
                RawJson = rawJson
            };
        }

        /// <summary>
        /// 反序列化远程端发送过来的 JSON 消息体。
        /// </summary>
        private RemoteMergeMessage DeserializeRemoteMessage(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                throw new InvalidOperationException("消息内容为空");
            }

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(RemoteMergeMessage));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(rawJson)))
            {
                return serializer.ReadObject(stream) as RemoteMergeMessage;
            }
        }

        /// <summary>
        /// 将远程请求加入队列，并在需要时启动队列处理。
        /// </summary>
        private void QueueRemoteMergeRequest(RemoteMergeRequest request)
        {
            if (request == null)
            {
                return;
            }

            remoteMergeQueue.Enqueue(request);
            lblStatus.Text = $"远程请求排队: {request.DisplayName}";

            if (!isProcessingRemoteQueue)
            {
                _ = ProcessRemoteMergeQueueCoreAsync();
            }
        }

        /// <summary>
        /// 顺序处理远程请求，避免多个对话框任务并发执行。
        /// </summary>
        private async Task ProcessRemoteMergeQueueCoreAsync()
        {
            if (isProcessingRemoteQueue)
            {
                return;
            }

            isProcessingRemoteQueue = true;
            try
            {
                while (remoteMergeQueue.Count > 0 && !IsDisposed)
                {
                    RemoteMergeRequest request = remoteMergeQueue.Dequeue();
                    lblStatus.Text = $"处理远程请求: {request.DisplayName}";

                    EnsureRemoteDialog();

                    bool requestAlreadyApplied = false;
                    if (request.RequirePreExecutionConfirmation)
                    {
                        activeRemoteDialog.ApplyRemoteRequest(request);
                        requestAlreadyApplied = true;
                        if (!activeRemoteDialog.TryPrepareRemoteRun())
                        {
                            lblStatus.Text = $"远程请求已取消: {activeRemoteDialog.GetStatusTextSnapshot()}";
                            continue;
                        }
                    }

                    bool completed = await RunRemoteRequestAsync(request, requestAlreadyApplied);
                    if (!completed && activeRemoteDialog != null)
                    {
                        lblStatus.Text = $"远程请求失败: {activeRemoteDialog.GetStatusTextSnapshot()}";
                    }
                }
            }
            finally
            {
                isProcessingRemoteQueue = false;
                DisposeRemoteDialog();
            }
        }

        /// <summary>
        /// 兼容 UI 线程与后台线程地安全更新状态栏文本。
        /// </summary>
        private void UpdateStatusSafely(string message)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatusSafely), message);
                return;
            }

            lblStatus.Text = message;
        }

        /// <summary>
        /// 确保可复用的远程套图对话框实例已创建。
        /// </summary>
        private void EnsureRemoteDialog()
        {
            if (activeRemoteDialog != null && !activeRemoteDialog.IsDisposed)
            {
                return;
            }

            activeRemoteDialog = new MergeDialog
            {
                Owner = this
            };
        }

        /// <summary>
        /// 通过套图对话框执行单个远程请求。
        /// </summary>
        private async Task<bool> RunRemoteRequestAsync(RemoteMergeRequest request, bool requestAlreadyApplied = false)
        {
            if (!requestAlreadyApplied)
            {
                activeRemoteDialog.ApplyRemoteRequest(request);
            }
            if (!activeRemoteDialog.Visible)
            {
                activeRemoteDialog.Show(this);
            }

            activeRemoteDialog.BringToFront();
            bool completed = await activeRemoteDialog.StartRemoteRunAsync();
            if (completed)
            {
                LoadDialogResultsToCanvasCore(activeRemoteDialog);
            }

            return completed;
        }

        /// <summary>
        /// 当远程请求队列空闲后释放可复用对话框。
        /// </summary>
        private void DisposeRemoteDialog()
        {
            if (activeRemoteDialog == null || activeRemoteDialog.IsDisposed)
            {
                activeRemoteDialog = null;
                return;
            }

            activeRemoteDialog.Dispose();
            activeRemoteDialog = null;
        }
    }
}
