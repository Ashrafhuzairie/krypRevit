using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace krypRevit.Addin.Chat
{
    public sealed class ChatForm : Form
    {
        private readonly ComboBox service=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill };
        private readonly TextBox model=new TextBox { Dock=DockStyle.Fill,MaxLength=200 };
        private readonly TextBox key=new TextBox { Dock=DockStyle.Fill,UseSystemPasswordChar=true,MaxLength=4096 };
        private readonly Button save=new Button { Text="Save key",AutoSize=true };
        private readonly Button forget=new Button { Text="Forget key",AutoSize=true };
        private readonly RichTextBox transcript=new RichTextBox { Dock=DockStyle.Fill,ReadOnly=true,WordWrap=true,BackColor=SystemColors.Window,BorderStyle=BorderStyle.FixedSingle,DetectUrls=false };
        private readonly TextBox input=new TextBox { Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,MaxLength=8000,AcceptsReturn=true };
        private readonly Button send=new Button { Text="Send",AutoSize=true };
        private readonly Button stop=new Button { Text="Stop",AutoSize=true,Enabled=false };
        private readonly Button review=new Button { Text="Review & apply",AutoSize=true,UseMnemonic=false };
        private readonly Button show3d=new Button {Text="Show in 3D",AutoSize=true};
        private readonly Button reset=new Button { Text="New chat",AutoSize=true };
        private readonly Label status=new Label { Text="Ready",AutoSize=true,Dock=DockStyle.Fill };
        private readonly ProviderClient provider=new ProviderClient();
        private readonly RevitChatTools tools;
        private readonly ChatRunner runner;
        private CancellationTokenSource cancellation;
        private bool busy;
        public ChatForm(Bridge bridge)
        {
            tools=new RevitChatTools(bridge);runner=new ChatRunner(provider,tools);
            Text="krypRevit — AI Assistant";Size=new Size(920,720);MinimumSize=new Size(720,540);
            StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;ShowIcon=false;
            var layout=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(12),ColumnCount=1,RowCount=6 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,90));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var options=new TableLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,ColumnCount=6,RowCount=2 };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            options.Controls.Add(new Label {Text="Provider",AutoSize=true,Anchor=AnchorStyles.Left},0,0);options.Controls.Add(service,1,0);
            options.Controls.Add(new Label {Text="Model",AutoSize=true,Anchor=AnchorStyles.Left},2,0);options.Controls.Add(model,3,0);options.SetColumnSpan(model,3);
            options.Controls.Add(new Label {Text="API key",AutoSize=true,Anchor=AnchorStyles.Left},0,1);options.Controls.Add(key,1,1);options.SetColumnSpan(key,3);
            options.Controls.Add(save,4,1);options.Controls.Add(forget,5,1);
            var notice=new Label {AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(0,6,0,8),Text="Messages and requested model information go to the selected AI provider. Model edits require your review and confirmation. Ctrl+Enter sends."};
            var buttons=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(0,4,0,4)};
            buttons.Controls.AddRange(new Control[]{send,stop,review,show3d,reset});
            layout.Controls.Add(options,0,0);layout.Controls.Add(notice,0,1);layout.Controls.Add(transcript,0,2);
            layout.Controls.Add(input,0,3);layout.Controls.Add(buttons,0,4);layout.Controls.Add(status,0,5);Controls.Add(layout);
            service.Items.AddRange(new object[]{"OpenAI","Claude","Gemini"});
            service.SelectedIndexChanged += (s,e) => LoadProvider();
            model.TextChanged += (s,e) => { if(!busy) runner.Reset(); };
            save.Click += (s,e) => SaveKey();forget.Click += (s,e) => ForgetKey();
            send.Click += async (s,e) => await SendAsync();stop.Click += (s,e) => cancellation?.Cancel();
            review.Click += async (s,e) => await ReviewAsync();
            show3d.Click += async (s,e) => await ShowIn3DAsync();
            reset.Click += (s,e) => {runner.Reset();transcript.Clear();Welcome();};
            input.KeyDown += async (s,e) => {if(e.Control && e.KeyCode==Keys.Enter) {e.SuppressKeyPress=true;await SendAsync();}};
            FormClosing += (s,e) => cancellation?.Cancel();
            FormClosed += (s,e) => {provider.Dispose();cancellation?.Dispose();key.Clear();};
            service.SelectedIndex=0;Welcome();
        }
        private string Service => (string)service.SelectedItem;
        private void Welcome() => Append("krypRevit","Ask me about your Revit model or request a supported edit. I can inspect and find elements, create walls/floors/levels, place loaded unhosted families, change instance types, reshape straight walls, and edit/move/rotate/copy elements. Click Review & apply to approve a proposal. Enter a provider API key to begin; saving it is optional.");
        private void LoadProvider()
        {
            runner.Reset();key.Clear();model.Text=ProviderClient.DefaultModel(Service);
            try {model.Text=KeyStore.Model(Service);key.Text=KeyStore.Load(Service);status.Text=key.Text.Length>0 ? "Saved key loaded for "+Service : "Enter an API key for "+Service;}
            catch {status.Text="Could not load saved settings. Enter a key; saving is optional.";}
        }
        private void SaveKey()
        {
            if(string.IsNullOrWhiteSpace(key.Text)) {status.Text="Enter a key first.";return;}
            try {KeyStore.Save(Service,key.Text.Trim(),model.Text.Trim());status.Text="Key saved encrypted for your Windows user.";}
            catch {status.Text="Could not save the key. You can still use it for this session.";}
        }
        private void ForgetKey()
        {
            try {KeyStore.Remove(Service);key.Clear();status.Text="Saved key removed for "+Service;}
            catch {status.Text="Could not remove the saved key. Check access to your local profile.";}
        }
        private void SetBusy(bool value)
        {
            busy=value;send.Enabled=!value;review.Enabled=!value;reset.Enabled=!value;service.Enabled=!value;
            show3d.Enabled=!value;model.Enabled=!value;key.Enabled=!value;save.Enabled=!value;forget.Enabled=!value;input.Enabled=!value;stop.Enabled=value;
        }
        private void Append(string role,string text)
        {
            if(IsDisposed || Disposing) return;
            if(InvokeRequired) {BeginInvoke(new Action(()=>Append(role,text)));return;}
            if(transcript.TextLength>250000) {transcript.Select(0,50000);transcript.SelectedText="";}
            transcript.AppendText(role+Environment.NewLine+text+Environment.NewLine+Environment.NewLine);
            transcript.SelectionStart=transcript.TextLength;transcript.ScrollToCaret();
        }
        private async Task SendAsync()
        {
            if(busy || string.IsNullOrWhiteSpace(input.Text)) return;
            if(string.IsNullOrWhiteSpace(key.Text) || string.IsNullOrWhiteSpace(model.Text)) {status.Text="Enter a provider API key and model name.";return;}
            var text=input.Text.Trim();var apiKey=key.Text.Trim();var selected=Service;var selectedModel=model.Text.Trim();
            input.Clear();Append("You",text);SetBusy(true);status.Text="Asking "+selected+"…";
            cancellation?.Dispose();cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(4));
            try {await runner.RunAsync(selected,selectedModel,apiKey,text,Append,cancellation.Token);if(!IsDisposed) status.Text="Ready";}
            catch(OperationCanceledException) {Append("krypRevit","Request stopped or timed out. Use Review & apply to check any proposal already staged. No chat tool can apply edits automatically.");}
            catch(HttpRequestException) {Append("Connection","Could not reach "+selected+". Check your company network/proxy and approved provider access.");}
            catch(Exception ex) {Append("Error",ex.Message.Replace(apiKey,"[redacted]"));}
            finally {if(!IsDisposed) {SetBusy(false);status.Text="Ready";input.Focus();}}
        }
        private async Task ShowIn3DAsync()
        {
            if(busy) return;SetBusy(true);stop.Enabled=false;
            cancellation?.Dispose();cancellation=new CancellationTokenSource();
            try {var result=Json.Deserialize<Dictionary<string,object>>(await tools.ShowIn3DAsync(cancellation.Token));Append("Revit","Selected "+Data.Get(result,"count")+" result element(s) in "+Data.Text(result,"view")+".");}
            catch(Exception ex) {Append("Revit",ex.Message);}
            finally {if(!IsDisposed) {SetBusy(false);status.Text="Ready";}}
        }
        private async Task ReviewAsync()
        {
            if(busy) return;SetBusy(true);stop.Enabled=false;status.Text="Waiting for Revit review…";
            cancellation?.Dispose();cancellation=new CancellationTokenSource();
            try
            {
                var result=await tools.ReviewAsync(cancellation.Token);runner.ReviewResult(result);
                var data=Json.Deserialize<Dictionary<string,object>>(result);var state=Data.Text(data,"state");
                string text;
                switch(state)
                {
                    case "applied":text=$"Committed {Data.Items(Data.Get(data,"created_unique_ids")).Count()} created and {Data.Items(Data.Get(data,"modified_unique_ids")).Count()} modified element(s). Click Show in 3D to inspect physical results; Revit Undo can undo the batch.";if(!string.IsNullOrEmpty(Data.Text(data,"result_warning"))) text+=" "+Data.Text(data,"result_warning");break;
                    case "cancelled":text="Proposal cancelled. No changes applied.";break;
                    case "stale":text="Proposal expired or the document changed. Ask for a fresh proposal.";break;
                    case "failed":text="Batch failed: "+Data.Text(data,"error");break;
                    case "pending_review":text="Proposal is still awaiting review.";break;
                    default:text="No proposal is waiting for review.";break;
                }
                Append("Review",text);
            }
            catch(Exception ex) {Append("Review",ex.Message);}
            finally {if(!IsDisposed) {SetBusy(false);status.Text="Ready";}}
        }
    }
}
