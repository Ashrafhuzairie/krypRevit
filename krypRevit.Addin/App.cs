using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Events;
using krypRevit.Addin.Endpoints;
using krypRevit.Addin.Chat;

namespace krypRevit.Addin
{
    /// <summary>
    /// Entry point. Loaded by Revit via krypRevit.addin.
    /// Creates the built-in AI assistant and optional localhost bridge.
    /// </summary>
    public class App : IExternalApplication
    {
        public const string TabName = "krypRevit";
        public static Bridge Bridge { get; private set; }
        public static HttpServer Server { get; private set; }
        private static ChatForm chat;

        public static void ShowChat(UIApplication app)
        {
            if(chat==null || chat.IsDisposed) chat=new ChatForm(Bridge);
            if(!chat.Visible) chat.Show(new RevitOwner(app.MainWindowHandle));
            if(chat.WindowState==System.Windows.Forms.FormWindowState.Minimized) chat.WindowState=System.Windows.Forms.FormWindowState.Normal;
            chat.BringToFront();chat.Activate();
        }
        private sealed class RevitOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; }
            public RevitOwner(IntPtr handle) {Handle=handle;}
        }

        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                // OnStartup runs on Revit's main thread, so ExternalEvent.Create is safe here.
                Bridge = new Bridge();

                app.CreateRibbonTab(TabName);
                var panel = app.CreateRibbonPanel(TabName, "AI Assistant");
                var chatButton=new PushButtonData("krypRevitChat","AI Chat",typeof(App).Assembly.Location,typeof(ShowChatCommand).FullName);
                chatButton.ToolTip="Open the built-in AI assistant (OpenAI, Claude or Gemini). No Python required.";
                RibbonIcons.Apply(chatButton,"Chat");panel.AddItem(chatButton);
                var data = new PushButtonData(
                    "krypRevitStatus", "MCP Status",
                    typeof(App).Assembly.Location, typeof(StatusCommand).FullName);
                data.ToolTip = "Show whether the krypRevit MCP bridge is running";
                RibbonIcons.Apply(data,"Status");
                panel.AddItem(data);
                var review = new PushButtonData("krypRevitReview", "Review & apply", typeof(App).Assembly.Location, typeof(ReviewCommand).FullName);
                review.ToolTip = "Review and approve the AI's proposed model changes";
                RibbonIcons.Apply(review,"Review");
                panel.AddItem(review);
                app.ControlledApplication.DocumentChanged += OnDocumentChanged;

                Server = new HttpServer(Bridge, port: 5060);
                try {Server.Start();}
                catch(Exception ex) {TaskDialog.Show("krypRevit","Built-in AI Chat is ready. The optional external MCP listener could not start: "+ex.Message);}
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Server?.Stop();
                Bridge?.Dispose();
                app.ControlledApplication.DocumentChanged -= OnDocumentChanged;
                TaskDialog.Show("krypRevit", "Startup failed:\n" + ex);
                return Result.Failed;
            }
        }

        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e) => Plans.DocumentChanged();

        public Result OnShutdown(UIControlledApplication app)
        {
            app.ControlledApplication.DocumentChanged -= OnDocumentChanged;
            chat?.Close();
            Plans.Clear();
            Server?.Stop();
            Bridge?.Dispose();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class StatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var running = App.Server != null && App.Server.IsRunning;
            TaskDialog.Show("krypRevit",
                running ? "Built-in AI Chat is ready. External MCP bridge running on http://127.0.0.1:5060" : "Built-in AI Chat is ready. Optional external MCP bridge is not running.");
            return Result.Succeeded;
        }
    }
}

namespace krypRevit.Addin
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ReviewCommand : Autodesk.Revit.UI.IExternalCommand
    {
        public Autodesk.Revit.UI.Result Execute(Autodesk.Revit.UI.ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try { Endpoints.Plans.Review(data.Application); return Autodesk.Revit.UI.Result.Succeeded; }
            catch (System.Exception ex) { message=ex.Message; return Autodesk.Revit.UI.Result.Failed; }
        }
    }
}

namespace krypRevit.Addin
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class ShowChatCommand : Autodesk.Revit.UI.IExternalCommand
    {
        public Autodesk.Revit.UI.Result Execute(Autodesk.Revit.UI.ExternalCommandData data,ref string message,Autodesk.Revit.DB.ElementSet elements)
        {
            try {App.ShowChat(data.Application);return Autodesk.Revit.UI.Result.Succeeded;}
            catch(System.Exception ex) {message=ex.Message;return Autodesk.Revit.UI.Result.Failed;}
        }
    }
}
