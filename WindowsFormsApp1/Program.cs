using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Aspose.PSD;
using System.Text;


namespace WindowsFormsApp1
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        /// 
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var loginDialog = new LoginDialog())
            {
                if (loginDialog.ShowDialog() != DialogResult.OK)
                    return;
            }

            Application.Run(new Form1());
        }
    }
}
