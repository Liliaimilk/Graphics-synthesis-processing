using System;
using System.IO;
using System.Text;

namespace WindowsFormsApp1
{
    /// <summary>
    /// 套图失败日志所需的上下文信息。
    /// </summary>
    internal sealed class MergeFailureLogEntry
    {
        public string Stage { get; set; }
        public string TaskName { get; set; }
        public string TemplateName { get; set; }
        public string TemplatePath { get; set; }
        public string MaterialName { get; set; }
        public string MaterialPath { get; set; }
        public string TemplateLayerName { get; set; }
        public string MaterialLayerName { get; set; }
        public string OutputPath { get; set; }
        public string OutputFormat { get; set; }
        public string CompositeModeName { get; set; }
        public bool? IsDoubleSided { get; set; }
        public string FailureMessage { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// 统一写入套图失败日志。日志写入失败不会中断当前套图任务。
    /// </summary>
    internal static class MergeFailureLogger
    {
        private static readonly object WriteLock = new object();

        /// <summary>
        /// 将失败信息追加到程序目录下按日期归档的套图失败日志。
        /// </summary>
        public static void Write(MergeFailureLogEntry entry)
        {
            try
            {
                string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logFolder);
                string logPath = Path.Combine(logFolder, "merge-failures-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

                var content = new StringBuilder();
                content.AppendLine(new string('=', 80));
                content.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                content.AppendLine("阶段: " + (entry?.Stage ?? "未知"));
                content.AppendLine("任务名称: " + (entry?.TaskName ?? "未命名任务"));
                content.AppendLine("请求组合名: " + (entry?.TaskName ?? "未提供"));
                content.AppendLine("模板名称: " + (entry?.TemplateName ?? "未识别"));
                content.AppendLine("模板路径: " + (entry?.TemplatePath ?? "未提供"));
                content.AppendLine("素材名称: " + (entry?.MaterialName ?? "未识别"));
                content.AppendLine("素材路径: " + (entry?.MaterialPath ?? "未提供"));
                content.AppendLine("模板图层: " + (entry?.TemplateLayerName ?? "单面/全部可见图层"));
                content.AppendLine("素材图层: " + (entry?.MaterialLayerName ?? "单面原图"));
                content.AppendLine("输出路径: " + (entry?.OutputPath ?? "未生成"));
                content.AppendLine("输出格式: " + (entry?.OutputFormat ?? "未知"));
                content.AppendLine("套图模式: " + (entry?.CompositeModeName ?? "未知"));
                content.AppendLine("双面模式: " + (entry?.IsDoubleSided?.ToString() ?? "未知"));
                content.AppendLine("错误信息: " + (entry?.FailureMessage ?? entry?.Exception?.Message ?? "未知错误"));

                if (entry?.Exception != null)
                {
                    content.AppendLine("异常类型: " + entry.Exception.GetType().FullName);
                    content.AppendLine("异常堆栈:");
                    content.AppendLine(entry.Exception.StackTrace ?? "无堆栈信息");

                    if (entry.Exception.InnerException != null)
                    {
                        content.AppendLine("内部异常: " + entry.Exception.InnerException.GetType().FullName);
                        content.AppendLine(entry.Exception.InnerException.Message);
                        content.AppendLine(entry.Exception.InnerException.StackTrace ?? "无内部异常堆栈");
                    }
                }

                lock (WriteLock)
                {
                    File.AppendAllText(logPath, content.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception logException)
            {
                Console.WriteLine("写入套图失败日志时出错: " + logException.Message);
            }
        }
    }
}
