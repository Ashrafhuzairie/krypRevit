using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
namespace krypRevit.Addin.Openings
{
    public sealed class OpeningRulesForm:Form
    {
        private readonly DataGridView rect=new DataGridView {Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
        private readonly DataGridView round=new DataGridView {Dock=DockStyle.Fill,AllowUserToAddRows=true,AllowUserToDeleteRows=true,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
        private readonly ComboBox matching=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=200};
        private readonly NumericUpDown tolerance=new NumericUpDown {DecimalPlaces=2,Minimum=0,Maximum=5,Increment=.1m,Value=.5m,Width=70};
        private readonly TextBox marker=new TextBox {Width=160};
        private readonly Label status=new Label {AutoSize=true,Dock=DockStyle.Fill,Text="Rules affect new plans; existing cuts are not resized. Circular opening maximum: 200 mm."};
        public OpeningRulesForm()
        {
            Text="krypRevit — Opening rules (mm)";Size=new Size(840,650);MinimumSize=new Size(700,550);StartPosition=FormStartPosition.CenterParent;ShowIcon=false;
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(10),ColumnCount=1,RowCount=6};
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,45));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,55));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label {AutoSize=true,Text="Rectangular clearances per side (mm). Wall X=along wall, Y=up; slab X/Y=internal plan axes. Width/height include both sides."},0,0);
            rect.Columns.Add(new DataGridViewTextBoxColumn {Name="Kind",ReadOnly=true});rect.Columns.Add(new DataGridViewCheckBoxColumn {Name="Enabled"});
            foreach(var name in new[]{"Left","Right","Top","Bottom"})rect.Columns.Add(name,name+" mm");
            layout.Controls.Add(rect,0,1);
            var options=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill};
            matching.Items.AddRange(new object[]{"Exact table match","Next larger service row"});
            options.Controls.AddRange(new Control[]{new Label {AutoSize=true,Text="Circular matching"},matching,new Label {AutoSize=true,Text="Tolerance mm"},tolerance,new Label {AutoSize=true,Text="Trunking type contains"},marker});
            layout.Controls.Add(options,0,2);round.Columns.Add("Service","Service diameter mm");round.Columns.Add("Opening","Opening diameter mm (max 200)");layout.Controls.Add(round,0,3);
            var buttons=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill};var save=new Button {Text="Save rules",AutoSize=true};var defaults=new Button {Text="Load defaults",AutoSize=true};var cancel=new Button {Text="Cancel",AutoSize=true};
            buttons.Controls.AddRange(new Control[]{save,defaults,cancel});layout.Controls.Add(buttons,0,4);layout.Controls.Add(status,0,5);Controls.Add(layout);
            defaults.Click+=(s,e)=>{Populate(new OpeningRules());status.Text="Defaults loaded into the editor. Click Save rules to apply.";};
            cancel.Click+=(s,e)=>Close();save.Click+=(s,e)=>Save();
            try {Populate(OpeningRuleStore.Load());}catch(Exception ex){Populate(new OpeningRules());status.Text="Could not load saved rules: "+ex.Message+" Review defaults before saving.";}
        }
        private void Populate(OpeningRules rules)
        {
            rect.Rows.Clear();foreach(var item in new[]{new {Name="Cable tray",Rule=rules.cable_tray},new {Name="Trunking",Rule=rules.trunking},new {Name="Rectangular duct",Rule=rules.rectangular_duct}})
                rect.Rows.Add(item.Name,item.Rule.enabled,item.Rule.left_mm,item.Rule.right_mm,item.Rule.top_mm,item.Rule.bottom_mm);
            round.Rows.Clear();foreach(var row in rules.circular)round.Rows.Add(row.service_mm,row.opening_mm);
            matching.SelectedIndex=rules.unmatched_circle=="next_larger"?1:0;tolerance.Value=(decimal)rules.match_tolerance_mm;marker.Text=rules.trunking_type_contains;
        }
        private static double Number(object value)
        {if(value==null||!double.TryParse(Convert.ToString(value,CultureInfo.CurrentCulture),NumberStyles.Float,CultureInfo.CurrentCulture,out var n))throw new ArgumentException("Enter numeric millimetre values in every rule cell.");return n;}
        private RectangularRule Rect(int row)=>new RectangularRule {enabled=Convert.ToBoolean(rect.Rows[row].Cells[1].Value),left_mm=Number(rect.Rows[row].Cells[2].Value),right_mm=Number(rect.Rows[row].Cells[3].Value),top_mm=Number(rect.Rows[row].Cells[4].Value),bottom_mm=Number(rect.Rows[row].Cells[5].Value)};
        private void Save()
        {
            try
            {
                rect.EndEdit();round.EndEdit();
                var rules=new OpeningRules {cable_tray=Rect(0),trunking=Rect(1),rectangular_duct=Rect(2),trunking_type_contains=marker.Text.Trim(),unmatched_circle=matching.SelectedIndex==1?"next_larger":"exact",match_tolerance_mm=(double)tolerance.Value,
                    circular=round.Rows.Cast<DataGridViewRow>().Where(r=>!r.IsNewRow).Select(r=>new DiameterRule {service_mm=Number(r.Cells[0].Value),opening_mm=Number(r.Cells[1].Value)}).ToList()};
                OpeningRuleStore.Save(rules);DialogResult=DialogResult.OK;Close();
            }
            catch(Exception ex){status.Text=ex.Message;}
        }
    }
}
