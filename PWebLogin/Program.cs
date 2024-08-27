
using Microsoft.Win32;
using PuppeteerSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.PortableExecutable;

namespace PWebLogin
{
    internal class Program
    {
        static string m_loadedScript = "";
        static bool m_isFinished = false;
        static IPage m_page = null;

        static async Task Main(string[] args)
        {
            //var browserFetcher = new BrowserFetcher();
            //await browserFetcher.DownloadAsync();

            string url = "";
            string script = "";
            string user_name = "";
            string user_name_xpath = "";
            string password = "";
            string password_xpath = "";
            string submit_xpath = "";
            string cookies_file = "";
            string user_info_file = "";
            foreach (var arg in args)
            {
                if (!arg.StartsWith("-"))
                    continue;
                string s = arg.TrimStart('-');
                int idx = s.IndexOf('=');
                if (idx < 0)
                    continue;
                string key = s.Substring(0, idx);
                string val = s.Substring(idx + 1);

                if (key.Equals("url", StringComparison.OrdinalIgnoreCase))
                    url = val;
                else if (key.Equals("script", StringComparison.OrdinalIgnoreCase))
                    script = val;
                else if (key.Equals("user_name", StringComparison.OrdinalIgnoreCase))
                    user_name = val;
                else if (key.Equals("user_name_xpath", StringComparison.OrdinalIgnoreCase))
                    user_name_xpath = val;
                else if (key.Equals("password", StringComparison.OrdinalIgnoreCase))
                    password = val;
                else if (key.Equals("password_xpath", StringComparison.OrdinalIgnoreCase))
                    password_xpath = val;
                else if (key.Equals("submit_xpath", StringComparison.OrdinalIgnoreCase))
                    submit_xpath = val;
                else if (key.Equals("cookies_file", StringComparison.OrdinalIgnoreCase))
                    cookies_file = val;
                else if (key.Equals("user_info_file", StringComparison.OrdinalIgnoreCase))
                    user_info_file = val;
            }
            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("缺少-url参数");
                return;
            }
            if (!string.IsNullOrEmpty(script))
            {
                m_loadedScript = await File.ReadAllTextAsync(script);
            }

            var opt = new LaunchOptions();
            opt.IgnoreHTTPSErrors = true;
            //opt.DumpIO = true;
            opt.Devtools = false;
            opt.Headless = false;
            opt.HeadlessMode = HeadlessMode.False;
            opt.DefaultViewport = null;//这个设置null让视口随窗口自适应

            //设置浏览器路径
            //opt.ExecutablePath = @"D:\Workspace\Git\WebLogin\WebLogin\bin\Debug\net6.0\chrome-win64\chrome.exe";
            //opt.ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
            //opt.ExecutablePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
            string? browserChoice = Registry.GetValue(
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice",
                "ProgId", "") as string;
            if (browserChoice == null)
            {
                Console.WriteLine("找不到浏览器");
                return;
            }
            string browserShellReg = @"HKEY_CLASSES_ROOT\" + browserChoice + @"\shell\open\command";
            string browserShell = Registry.GetValue(browserShellReg, "", "") as string;
            var cmds = CommandLineParser.ParseCommandLine(browserShell);
            if (cmds.Length == 0)
            {
                Console.WriteLine("找不到浏览器");
                return;
            }
            opt.ExecutablePath = cmds[0];

            //设置用户数据目录
            string userDataDir = Path.Combine(Path.GetTempPath(), "PWebLogin");
            opt.UserDataDir = userDataDir;

            List<string> chromeArgs = new List<string>();
            chromeArgs.Add("--user-agent=MDI");
            chromeArgs.Add("--no-sandbox");
            opt.Args = chromeArgs.ToArray();

            await using var browser = await Puppeteer.LaunchAsync(opt);
            
            var pages = await browser.PagesAsync();
            if (pages.Length == 0)
                m_page = await browser.NewPageAsync();
            else
                m_page = pages[0];

            //await m_page.ExposeFunctionAsync<string, string, bool>("write_file", write_file);
            //await m_page.ExposeFunctionAsync<string, string>("read_file", read_file);
            ////await m_page.ExposeFunctionAsync<string, string, Task<bool>>("input_value_by_fullpath", input_value_by_fullpath);
            ////await m_page.ExposeFunctionAsync<string, Task<bool>>("click_by_fullpath", click_by_fullpath);
            ////await m_page.ExposeFunctionAsync<string>("get_cookies", get_cookies);
            //await m_page.ExposeFunctionAsync("set_login_finished", set_login_finished);

            ////await m_page.SetExtraHttpHeadersAsync();
            //m_page.Load += HtmpPage_Load;
            ////m_page.DOMContentLoaded += HtmlPage_DOMContentLoaded;

            //m_page.Response += page_Response;

            try
            {
                //浏览
                await m_page.GoToAsync(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine("打开网址失败:" + url);
                await ExitBroswerAsync();
                return;
            }
            //await m_page.ScreenshotAsync("E:\\1.png");

            if (!string.IsNullOrEmpty(m_loadedScript))
                await m_page.EvaluateExpressionAsync(m_loadedScript);

            //准备
            await m_page.DeleteCookieAsync();
            await m_page.EvaluateExpressionAsync(@"window.localStorage.clear();");

            //自动登录
            if (user_name != "" && user_name_xpath != "")
            {
                var eles = await m_page.XPathAsync(user_name_xpath);
                if (eles != null)
                {
                    foreach ( var ele in eles )
                    {
                        await ele.TypeAsync(user_name);
                    }
                }
            }
            if (password != "" && password_xpath != "")
            {
                var eles = await m_page.XPathAsync(password_xpath);
                if (eles != null)
                {
                    foreach (var ele in eles)
                    {
                        await ele.TypeAsync(password);
                    }
                }
            }
            if (submit_xpath != "")
            {
                var eles = await m_page.XPathAsync(submit_xpath);
                if (eles != null)
                {
                    foreach (var ele in eles)
                    {
                        await ele.ClickAsync();
                    }
                }
            }

            while (!m_isFinished)
            {
                if (m_page.IsClosed)
                    break;

                Console.WriteLine("等待登录完成...");

                CookieParam[] cookies = null;
                string userConfig = "";
                try
                {
                    //获得cookies
                    cookies = await m_page.GetCookiesAsync(new string[] { m_page.Url });

                    //获得userConfig
                    var localStorage = await m_page.EvaluateExpressionAsync<string>(@"window.localStorage.userConfig;");
                    if (localStorage != null)
                    {
                        Console.WriteLine("1.获得userConfig:" + localStorage);
                        userConfig = localStorage;
                    }
                }
                catch (Exception)
                { }
                if (cookies != null && cookies.Length > 0 && !string.IsNullOrWhiteSpace(userConfig))
                {
                    if (!string.IsNullOrEmpty(cookies_file))
                    {
                        string cookiesText = "";
                        foreach (var cookie in cookies)
                        {
                            if (cookiesText != "")
                                cookiesText += "; ";
                            cookiesText += cookie.Name + "=" + cookie.Value;
                        }
                        Console.WriteLine("2.获得Cookies:" + cookiesText);
                        File.WriteAllText(cookies_file, cookiesText);
                    }
                    if (!string.IsNullOrEmpty(user_info_file))
                        File.WriteAllText(user_info_file, userConfig);
                    break;
                }

                Thread.Sleep(1000);
            }

            await ExitBroswerAsync();

            //#region //非正常关闭，但能快速关闭
            ////browser.Process.Kill();
            //browser.Process.CloseMainWindow();
            //Process.GetCurrentProcess().Kill();
            //#endregion
            //await m_page.DisposeAsync();

            //Console.ReadKey();
        }

        private static async Task ExitBroswerAsync()
        {
            #region //非正常关闭，但能快速关闭
            //browser.Process.Kill();
            m_page.Browser.Process.CloseMainWindow();
            Process.GetCurrentProcess().Kill();
            #endregion

            await m_page.DisposeAsync();
        }

        //private static void page_Response(object? sender, ResponseCreatedEventArgs e)
        //{
        //    if (e.Response.Headers.TryGetValue("Set-Cookie", out var cookies))
        //    {
        //        Console.WriteLine("Set-Cookie: " + cookies);
        //    }
        //}

        //private static async Task<bool> input_value_by_fullpath(string xfullPath, string text)
        //{
        //    if (m_page == null)
        //        return false;
        //    var eles = await m_page.XPathAsync(xfullPath);
        //    if (eles.Length == 0)
        //        return false;

        //    foreach (var ele in eles)
        //    {
        //        await ele.TypeAsync(text);
        //    }
        //    return true;
        //}

        //private static async Task<bool> click_by_fullpath(string xfullPath)
        //{
        //    if (m_page == null)
        //        return false;
        //    var eles = await m_page.XPathAsync(xfullPath);
        //    if (eles.Length == 0)
        //        return false;

        //    foreach (var ele in eles)
        //    {
        //        await ele.ClickAsync();
        //    }
        //    return true;
        //}

        //private static string get_cookies()
        //{
        //    if (m_page == null)
        //        return "";
        //    var t_cookies = m_page.GetCookiesAsync(new string[] { m_page.Url });
        //    t_cookies.Wait();
        //    return Newtonsoft.Json.JsonConvert.SerializeObject(t_cookies.Result);
        //}

        //private static bool write_file(string path, string contents)
        //{
        //    File.WriteAllText(path, contents);
        //    return true;
        //}

        //private static string read_file(string path)
        //{
        //    return File.ReadAllText(path);
        //}

        //private static void set_login_finished()
        //{
        //    m_isFinished = true;
        //}

        ////private static void HtmlPage_DOMContentLoaded(object? sender, EventArgs e)
        ////{

        ////}

        //private static void HtmpPage_Load(object? sender, EventArgs e)
        //{
        //    IPage page = sender as IPage;
        //    if (page == null)
        //        return;

        //    page.EvaluateExpressionAsync(m_loadedScript);
        //}
    }

    public class CommandLineParser
    {
        public static string[] ParseCommandLine(string commandLine)
        {
            var args = new List<string>();
            int i = 0;
            bool inQuote = false;
            while (i < commandLine.Length)
            {
                // 跳过前面的空白字符
                while (i < commandLine.Length && char.IsWhiteSpace(commandLine[i]))
                {
                    i++;
                }
                if (i == commandLine.Length)
                {
                    break;
                }

                // 参数内容
                int startIndex = i;
                if (commandLine[i] == '"')
                {
                    inQuote = true;
                    i++;
                    startIndex = i;
                    while (i < commandLine.Length && commandLine[i] != '"')
                    {
                        i++;
                    }
                }
                else
                {
                    while (i < commandLine.Length && !char.IsWhiteSpace(commandLine[i])
                        && commandLine[i] != '"')
                    {
                        i++;
                    }
                }

                if (inQuote && i < commandLine.Length && commandLine[i] == '"')
                {
                    i++; // 跳过结束的引号
                }

                string arg = commandLine.Substring(startIndex, i - startIndex).Trim('"');
                if (arg.Length > 0)
                {
                    args.Add(arg);
                }

                inQuote = false;
            }

            return args.ToArray();
        }
    }
}