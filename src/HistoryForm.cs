using System.Globalization;
namespace CodexUsageMonitor;

internal sealed class HistoryForm:ProductForm {
 readonly MonitorService service;
 readonly R2Grid grid=new();
 readonly Label title=Theme.Label("",48,22),status=Theme.Label("",36,9,Theme.Muted),detail=Theme.Label("",94,9,Theme.Muted);
 readonly Label dateError=Theme.Label("",28,9,Theme.Warning),exportHint=Theme.Label("",30,9,Theme.Muted);
 readonly DateField from=new(),to=new();
 readonly CheckBox all=Theme.Check("");
 readonly DarkChoice pool=new(){Width=150},window=new(){Width=126},quality=new(){Width=156},eventFilter=new(){Width=186};
 readonly TrendView trend=new(){Dock=DockStyle.Top,Height=200,Tag=200};
 readonly EventFocusView eventView=new(){Visible=false};
 readonly GoldActionButton goldAction=new(){Visible=false};
 readonly FlowLayoutPanel goldActions=Theme.Row();
 readonly GoldEventRail goldRail;
 Control? recordsSection,historyDivider;
 readonly Label loadingShell=Theme.Label("",34,10,Theme.Muted);
 readonly Panel advanced=new(){Dock=DockStyle.Top,Height=44,Tag=44,Visible=false};
 readonly Button previous,next,latest,full,eventFocus,resetViewport;
 readonly Dictionary<Control,string> bindings=[];
 readonly List<Control> exportControls=[];
 readonly Dictionary<string,UsageSample?> previousById=[];
 List<UsageSample> rows=[],visibleRows=[],chartFocusRows=[];List<UsageEvent> events=[],chartFocusEvents=[];
 HistoryFilter? activeFilter;CancellationTokenSource? query;int page,request,sortColumn,timeZoneRevision;bool sortAscending,busy,relabeling;
 string statusKey="History.Loading";object[] statusArgs=[];Action? relayoutToolbars;
 const int PageSize=200;
 string? pendingFocusSample;
 public HistoryForm(MonitorService s,DateTime? date=null,string? focusSampleId=null):base(L.T("History.Title"),1120,920){
  pendingFocusSample=focusSampleId;goldRail=new GoldEventRail(trend){Visible=false};
  service=s;timeZoneRevision=L.TimeZoneRevision;MinimumSize=new(760,560);from.Value=date??DateTime.Today.AddDays(-6);to.Value=date??DateTime.Today;
  pool.Items.Add("codex");foreach(var id in s.Current?.Windows.Select(w=>w.LimitId).Distinct().Where(x=>x!="codex").OrderBy(x=>x.Contains("bengalfox")?1:0).ToList()??[])pool.Items.Add(id);pool.SelectedIndex=0;
  window.Items.AddRange(["","",""]);window.SelectedIndex=0;quality.Items.AddRange(["","","",""]);quality.SelectedIndex=0;eventFilter.Items.AddRange(["","","",""]);eventFilter.SelectedIndex=0;
  foreach(var c in new Control[]{pool,window,quality,eventFilter})Theme.Style(c);
  var advancedRow=Theme.Row(pool,window,quality,eventFilter,all);advanced.Controls.Add(advancedRow);
  advanced.SizeChanged+=(_,_)=>relayoutToolbars?.Invoke();
  pool.SelectedIndexChanged+=async(_,_)=>{if(!relabeling){page=0;await LoadPage();}};
  window.SelectedIndexChanged+=async(_,_)=>{if(!relabeling){page=0;await LoadPage();}};
  quality.SelectedIndexChanged+=(_,_)=>{if(!relabeling){page=0;SelectVisible();}};
  eventFilter.SelectedIndexChanged+=(_,_)=>{if(!relabeling){page=0;SelectVisible();}};
  all.CheckedChanged+=(_,_)=>{if(!relabeling){page=0;SelectVisible();}};
  var dateRow=Theme.Row();dateRow.Name="HistoryFilters";
  foreach(var (key,days,width) in new[]{("Today",0,72),("Yesterday",1,96),("SevenDays",6,76),("ThirtyDays",29,84)}){
   var quick=Button(key,async(_,_)=>{from.Value=DateTime.Today.AddDays(-days);to.Value=days==1?DateTime.Today.AddDays(-1):DateTime.Today;page=0;await LoadPage();},width);dateRow.Controls.Add(quick);
  }
  var apply=Button("Apply",async(_,_)=>{page=0;await LoadPage();},96);
  var more=Button("Advanced",(_,_)=>{advanced.Visible=!advanced.Visible;detail.Visible=advanced.Visible;},104);
  var separator=new Label{Text="–",AccessibleName="–",Width=14,Height=30,TextAlign=ContentAlignment.MiddleCenter};
  dateRow.Controls.AddRange([from,separator,to,apply,more]);
  dateRow.SizeChanged+=(_,_)=>relayoutToolbars?.Invoke();
  dateError.Visible=false;from.TextChanged+=(_,_)=>dateError.Visible=false;to.TextChanged+=(_,_)=>dateError.Visible=false;
  ConfigureGrid();
  grid.SelectionChanged+=(_,_)=>UpdateDetail();
  grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)OpenSelectedEventFocus();};
  grid.ColumnHeaderMouseClick+=(_,e)=>{if(e.ColumnIndex<0)return;sortAscending=sortColumn==e.ColumnIndex?!sortAscending:true;sortColumn=e.ColumnIndex;RenderGrid();};
  detail.Visible=false;
  latest=Button("LatestSegment",(_,_)=>SetChartMode(true),156);
  full=Button("FullRange",(_,_)=>SetChartMode(false),126);
  eventFocus=Button("EventFocus",(_,_)=>OpenSelectedEventFocus(),132);
  resetViewport=Button("ResetViewport",(_,_)=>trend.ResetViewport(),142);
  var chartTitle=Theme.Label("",36,12);chartTitle.Dock=DockStyle.None;chartTitle.Width=214;bindings[chartTitle]="History.ChartTitle";
  var chartBar=Theme.Row(chartTitle,latest,full,eventFocus,resetViewport);chartBar.Name="HistoryChartViews";
  chartBar.SizeChanged+=(_,_)=>relayoutToolbars?.Invoke();
  bool fittingToolbars=false;
  relayoutToolbars=()=>{
   if(fittingToolbars)return;fittingToolbars=true;try{
   float scale=DeviceDpi/96f;
   void Fit(FlowLayoutPanel row,int breakpoint){
    bool wrap=row.Width<breakpoint*scale;int height=(int)((wrap?88:44)*scale);if(row.WrapContents!=wrap)row.WrapContents=wrap;if(row.Height!=height)row.Height=height;
   }
   Fit(dateRow,940);Fit(advancedRow,885);if(advanced.Height!=advancedRow.Height)advanced.Height=advancedRow.Height;Fit(chartBar,898);
   }finally{fittingToolbars=false;}
  };
  trend.Latest=s.Settings.ChartView!="FULL";
  previous=Button("PreviousPage",(_,_)=>{page=Math.Max(0,page-1);RenderGrid();},104);
  next=Button("NextPage",(_,_)=>{if((page+1)*PageSize<visibleRows.Count)page++;RenderGrid();},88);
  var exportLog=Button("ExportAnalysis",(_,_)=>new ExportForm(service,Filter()).ShowDialog(this),192,true);
  var exportCsv=Button("ExportCSV",async(_,_)=>await ExportCsv(),122);
  var note=Button("AddNote",(_,_)=>Note(),110);
  Footer.Controls.AddRange([exportLog,exportCsv,note,next,previous]);exportControls.AddRange([exportLog,exportCsv,note]);foreach(var c in exportControls)c.Enabled=false;
  ((QuietButton)more).Role=ButtonRole.Ghost;((QuietButton)resetViewport).Role=ButtonRole.Ghost;
  ((QuietButton)previous).Role=ButtonRole.Ghost;((QuietButton)next).Role=ButtonRole.Ghost;
  ((QuietButton)apply).Primary=true;
  var divider=new HistoryDivider(grid,trend);
  historyDivider=divider;recordsSection=Theme.Section(R2.Caption("▤  Usage records","▤  觀測紀錄",26),loadingShell,grid);
  goldAction.Dock=DockStyle.None;goldActions.Controls.Add(goldAction);goldActions.Height=64;goldActions.Tag=64;goldActions.Visible=false;
  Stack(title,R2.Caption("Every observation leaves a trace. Explore your quota, with evidence.","每次觀測都有跡可循。從歷史與證據，看清額度變化。"),Theme.Section(dateRow,dateError,advanced),recordsSection,divider,Theme.Section(chartBar,goldRail,trend,eventView,goldActions),detail,status,exportHint);
  trend.TabStop=true;trend.GoldEventSelected+=id=>{var item=trend.GoldEvents.FirstOrDefault(x=>x.id==id);if(item!=null)OpenEventFocusForSample(item.after_sample_id);};
  goldAction.Click+=(_,_)=>{var item=eventView.Visible?eventView.Evidence?.LocalCycle:trend.GoldEvents.FirstOrDefault(x=>x.after_sample_id==(grid.CurrentRow?.Tag as UsageSample)?.sample_id&&x.Full)??trend.GoldEvents.LastOrDefault(x=>x.Full);if(item!=null)OpenEventFocusForSample(item.after_sample_id);};
  grid.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){OpenSelectedEventFocus();e.Handled=true;}};
  KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape&&eventView.Visible){SetChartMode(false);e.Handled=true;e.SuppressKeyPress=true;}};
  L.Watch(this,Localize);
  ActiveControl=from;Load+=async(_,_)=>await LoadPage();
  FormClosed+=(_,_)=>{query?.Cancel();query?.Dispose();query=null;};
 }
 Button Button(string key,EventHandler click,int width,bool primary=false){
  var b=Theme.Button(L.T("History."+key),click,primary);b.Width=width;bindings[b]="History."+key;return b;
 }
 void ConfigureGrid(){
  grid.Dock=DockStyle.Top;grid.Height=259;grid.Tag=259;grid.ReadOnly=true;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=false;
  grid.RowHeadersVisible=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
  grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.BorderStyle=BorderStyle.None;grid.BackgroundColor=Theme.Surface;grid.GridColor=Theme.Border;
  grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle=new(){BackColor=Theme.Surface2,ForeColor=Theme.Muted,Font=Theme.Font(9,true),Padding=new(10,0,10,0)};
  grid.DefaultCellStyle=new(){BackColor=Theme.Surface1,ForeColor=Theme.Text,SelectionBackColor=Theme.Selection,SelectionForeColor=Theme.Text,Font=Theme.Font(9.5f),Padding=new(10,0,10,0)};
  grid.AlternatingRowsDefaultCellStyle.BackColor=Theme.Surface1;grid.RowTemplate.Height=25;grid.CellBorderStyle=DataGridViewCellBorderStyle.None;
  grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;grid.ColumnHeadersHeight=34;
  grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Theme.Surface2;grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=Theme.Muted;
  foreach(var (key,weight) in new[]{("Time",23),("Used",12),("Remaining",13),("Change",24),("Status",28)})
   grid.Columns.Add(new DataGridViewTextBoxColumn{Name=key,HeaderText=L.T("History."+key),FillWeight=weight,SortMode=DataGridViewColumnSortMode.Programmatic});
  foreach(int i in new[]{1,2}){grid.Columns[i].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;grid.Columns[i].HeaderCell.Style.Alignment=DataGridViewContentAlignment.MiddleRight;}
  int hoverRow=-1;
  grid.CellMouseEnter+=(_,e)=>{int old=hoverRow;hoverRow=e.RowIndex;if(old>=0&&old<grid.Rows.Count)grid.InvalidateRow(old);if(hoverRow>=0&&hoverRow<grid.Rows.Count)grid.InvalidateRow(hoverRow);};
  grid.MouseLeave+=(_,_)=>{int old=hoverRow;hoverRow=-1;if(old>=0&&old<grid.Rows.Count)grid.InvalidateRow(old);};
  grid.CellFormatting+=(_,e)=>{if(e.RowIndex>=0&&e.CellStyle!=null)e.CellStyle.BackColor=e.RowIndex==hoverRow?Theme.Surface3:Theme.Surface1;};
  grid.RowPostPaint+=(_,e)=>{if((e.State&DataGridViewElementStates.Selected)!=0){using var stripe=new SolidBrush(Theme.Accent);e.Graphics.FillRectangle(stripe,e.RowBounds.Left,e.RowBounds.Top,3*DeviceDpi/96f,e.RowBounds.Height);}};
  grid.HandleCreated+=(_,_)=>NativeDark.Scrollbars(grid.Handle);
  // Match the cell text rectangle: no extra GDI glyph-overhang padding or hidden sort-glyph width.
  grid.CellPainting+=(_,e)=>{
   if(R2.Is(grid)||e.RowIndex!=-1||e.ColumnIndex<0||e.Graphics==null)return;e.PaintBackground(e.ClipBounds,false);
   int pad=(int)Math.Round(10*DeviceDpi/96f);var rect=e.CellBounds;rect.Inflate(-pad,0);
   TextRenderer.DrawText(e.Graphics,grid.Columns[e.ColumnIndex].HeaderText,grid.ColumnHeadersDefaultCellStyle.Font,rect,Theme.Muted,
    TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|(e.ColumnIndex is 1 or 2?TextFormatFlags.Right:TextFormatFlags.Left));
   e.Handled=true;
  };
 }
 protected override void OnLoad(EventArgs e){
  base.OnLoad(e);float k=DeviceDpi/96f;int padding=(int)Math.Round(10*k);
  grid.DefaultCellStyle.Padding=grid.ColumnHeadersDefaultCellStyle.Padding=new(padding,0,padding,0);
  grid.ColumnHeadersHeight=(int)(34*k);grid.RowTemplate.Height=(int)(25*k);
  foreach(DataGridViewRow row in grid.Rows)row.Height=(int)(25*k);
  // ProductForm restores logical Tag heights during initial DPI setup; apply responsive rows last.
  relayoutToolbars?.Invoke();
 }
 void Localize(){
  if(IsDisposed)return;bool timeZoneChanged=timeZoneRevision!=L.TimeZoneRevision;timeZoneRevision=L.TimeZoneRevision;
  relabeling=true;var preservedStatusKey=statusKey;var preservedStatusArgs=statusArgs;
  try{
   Text=AccessibleName=title.Text=title.AccessibleName=L.T("History.Title");
   foreach(var pair in bindings){pair.Key.Text=L.T(pair.Value);pair.Key.AccessibleName=pair.Key.Text;}
   from.AccessibleName=L.T("History.FromDate");to.AccessibleName=L.T("History.ToDate");
   pool.AccessibleName=L.T("History.Pool");window.AccessibleName=L.T("History.Window");quality.AccessibleName=L.T("History.Quality");eventFilter.AccessibleName=L.T("History.Events");
   all.Text=all.AccessibleName=L.T("History.AllSamples");
   SetItems(window,["Weekly","FiveHours","AllWindows"]);SetItems(quality,["AllQuality","ValidData","MonitoringGap","SourceIssue"]);SetItems(eventFilter,["AllEvents","Decrease","Increase","ContextChange"]);
   foreach(DataGridViewColumn c in grid.Columns)c.HeaderText=L.T("History."+c.Name);
   grid.AccessibleName=L.T("History.TableDescription");
   exportHint.Text=exportHint.AccessibleName=L.T("History.FullExportHint");
   if(dateError.Visible)dateError.Text=L.T("History."+dateError.Tag!.ToString());
   RenderGrid();UpdateDetail();UpdateModeButtons();
   statusKey=preservedStatusKey;statusArgs=preservedStatusArgs;status.Text=L.T(statusKey,statusArgs);
  }finally{relabeling=false;}
  // Locale changes only relabel the cached snapshot. A changed local zone rebases the same
  // visible Gregorian dates through the cancellable query, retaining page/focus where possible.
  if(timeZoneChanged&&IsHandleCreated&&!IsDisposed)_=LoadPage();
 }
 static void SetItems(DarkChoice choice,string[] keys){for(int i=0;i<keys.Length;i++)choice.Items[i]=L.T("History."+keys[i]);choice.Invalidate();}
 void SetStatus(string key,params object[] args){statusKey="History."+key;statusArgs=args;status.Text=L.T(statusKey,args);}
 void UpdateModeButtons(){
  if(latest is QuietButton a){a.Selected=trend.Latest&&!eventView.Visible;a.Invalidate();}
  if(full is QuietButton b){b.Selected=!trend.Latest&&!eventView.Visible;b.Invalidate();}
  if(eventFocus is QuietButton c){c.Selected=eventView.Visible;c.Invalidate();}
  resetViewport.Enabled=!trend.Latest&&!eventView.Visible;
  latest.AccessibleDescription=L.T(trend.Latest?"History.SelectedView":"History.SelectView");
  full.AccessibleDescription=L.T(!trend.Latest&&!eventView.Visible?"History.SelectedView":"History.SelectView");
  eventFocus.AccessibleDescription=L.T(eventView.Visible?"History.SelectedView":"History.SelectView");
 }
 internal void SetChartMode(bool useLatest){
  eventView.Visible=false;trend.Visible=true;goldRail.Visible=trend.GoldEvents.Any(x=>x.Full);goldActions.Visible=false;if(recordsSection!=null)recordsSection.Visible=true;if(historyDivider!=null)historyDivider.Visible=true;trend.Latest=useLatest;service.Settings.ChartView=useLatest?"LATEST":"FULL";service.SaveSettings();UpdateModeButtons();
 }
 internal static bool TryDateRange(string begin,string end,TimeZoneInfo zone,out DateTimeOffset first,out DateTimeOffset last,out string error){
  first=last=default;error="";
  if(!DateTime.TryParseExact(begin,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var a)||!DateTime.TryParseExact(end,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var b)){error="InvalidDate";return false;}
  if(a>b){error="ReversedDates";return false;}
  if(b.Year==9999&&b.Month==12&&b.Day==31){error="InvalidDate";return false;}
  first=DailyUsageCalculator.Boundary(DateOnly.FromDateTime(a),zone);
  last=DailyUsageCalculator.Boundary(DateOnly.FromDateTime(b).AddDays(1),zone).AddTicks(-1);return true;
 }
 bool ReadFilter(out HistoryFilter filter){
  if(!TryDateRange(from.Text,to.Text,TimeZoneInfo.Local,out var begin,out var end,out var error)){
   dateError.Tag=error;dateError.Text=L.T("History."+error);dateError.Visible=true;filter=activeFilter??new(DateTimeOffset.Now,DateTimeOffset.Now);return false;
  }
  dateError.Visible=false;filter=new(begin,end,pool.SelectedItem?.ToString()??"codex",window.SelectedIndex==0?10080:window.SelectedIndex==1?300:null);return true;
 }
 HistoryFilter Filter()=>activeFilter??new(DailyUsageCalculator.Boundary(DateOnly.FromDateTime(DateTime.Today),TimeZoneInfo.Local),DateTimeOffset.UtcNow,"codex",10080);
 internal HistoryFilter ExportRange=>Filter();
 internal IReadOnlyList<UsageSample> ChartRows=>trend.DisplayRows;
 internal TrendView TrendForTest=>trend;internal EventFocusEvidence? FocusEvidence=>eventView.Evidence;internal bool IsEventFocusVisible=>eventView.Visible;
 internal int DisplayRecordCount=>visibleRows.Count;
 internal int CurrentPage=>page;
 internal int QueryGeneration=>request;
 internal async Task LoadPage(){
  if(!ReadFilter(out var f))return;
  query?.Cancel();query?.Dispose();query=new();var token=query.Token;int generation=++request;
  busy=true;previous.Enabled=next.Enabled=false;foreach(var c in exportControls)c.Enabled=false;SetStatus("Loading");
  loadingShell.Text=L.T("History.Loading");loadingShell.Visible=true;
  try{
   // One bounded store snapshot per date/pool query. Page, language and chart switches use the snapshot.
   var data=await Task.Run(()=>{
    List<UsageSample> Read(HistoryFilter filter){var result=new List<UsageSample>();foreach(var row in service.History.Query<UsageSample>(filter)){token.ThrowIfCancellationRequested();result.Add(row);}token.ThrowIfCancellationRequested();result.Sort((a,b)=>a.observed_at_utc.CompareTo(b.observed_at_utc));return result;}
    var sampleRows=Read(f);var chartRows=f.Pool=="codex"&&f.Duration==10080?sampleRows:Read(f with{Pool="codex",Duration=10080});
    var eventRows=new List<UsageEvent>();foreach(var row in service.History.Query<UsageEvent>(f)){token.ThrowIfCancellationRequested();eventRows.Add(row);}
    var chartEvents=eventRows;if(f.Pool!="codex"||f.Duration!=10080){chartEvents=[];foreach(var row in service.History.Query<UsageEvent>(f with{Pool="codex",Duration=10080})){token.ThrowIfCancellationRequested();chartEvents.Add(row);}}
    return(samples:sampleRows,chart:chartRows,events:eventRows,chartEvents);
   },token);
   if(IsDisposed||generation!=request)return;
   eventView.Visible=false;eventView.Evidence=null;trend.Visible=true;if(recordsSection!=null)recordsSection.Visible=true;if(historyDivider!=null)historyDivider.Visible=true;
   rows=data.samples;events=data.events;chartFocusRows=data.chart;chartFocusEvents=data.chartEvents;activeFilter=f;previousById.Clear();UsageSample? previousSample=null;
   foreach(var row in rows){previousById[row.sample_id]=previousSample;previousSample=row;}
   trend.GoldEvents=LocalQuotaCycle.Derive(data.chart,DateTimeOffset.UtcNow);grid.GoldSampleIds=trend.GoldEvents.Where(x=>x.Full).Select(x=>x.after_sample_id).ToHashSet();trend.Rows=data.chart;goldRail.Visible=trend.GoldEvents.Any(x=>x.Full);goldActions.Visible=goldAction.Visible=false;loadingShell.Visible=false;busy=false;foreach(var c in exportControls)c.Enabled=true;SelectVisible();UpdateModeButtons();if(pendingFocusSample is {} id){pendingFocusSample=null;OpenEventFocusForSample(id);}
  }catch(OperationCanceledException){}
  catch{if(!IsDisposed&&generation==request){busy=false;SetStatus("ReadFailed");loadingShell.Text=L.T("History.ReadFailed");loadingShell.Visible=true;previous.Enabled=next.Enabled=false;}}
 }
 void SelectVisible(){
  if(busy)return;
  IEnumerable<UsageSample> selected=all.Checked?rows:rows.Where((s,i)=>i==0||s.remaining_percent!=rows[i-1].remaining_percent||!TrendView.Bridge(rows[i-1],s));
  selected=selected.Where(x=>quality.SelectedIndex switch{1=>x.data_quality=="VALID",2=>x.had_gap,3=>x.data_quality!="VALID",_=>true});
  if(eventFilter.SelectedIndex>0){
   var relevant=events.Where(e=>eventFilter.SelectedIndex switch{1=>e.event_type=="QUOTA_DECREASE_OBSERVED",2=>e.event_type.Contains("INCREASE")||e.event_type.Contains("REPLENISHMENT")||e.event_type.Contains("ZERO_TO_FULL"),_=>e.event_type.Contains("RESET_TIMESTAMP")||e.event_type.Contains("CONTEXT")}).Select(e=>e.current_sample_id).ToHashSet();
   selected=selected.Where(x=>relevant.Contains(x.sample_id));
  }
  visibleRows=selected.ToList();page=Math.Clamp(page,0,Math.Max(0,(visibleRows.Count-1)/PageSize));RenderGrid();
 }
 static string Percent(decimal? v)=>v is decimal n?TrendView.Number(n)+"%":"—";
 internal static string Change(UsageSample? previous,UsageSample current){
  if(previous==null||DailyUsageCalculator.Reject(previous,current).Length!=0)return"—";
  decimal delta=current.remaining_percent!.Value-previous.remaining_percent!.Value;
  return delta==0?L.T("History.Unchanged"):L.T("History.RemainingChange",(delta>0?"+":"−")+TrendView.Number(Math.Abs(delta)));
 }
 internal static string RowStatus(UsageSample? previous,UsageSample current){
  if(!DailyUsageCalculator.Valid(current))return L.T("History.UnavailableObservation");
  if(previous==null)return L.T("History.Started");
  var reasons=DailyUsageCalculator.Reject(previous,current);
  if(current.had_gap||reasons.Contains("LONG_GAP"))return L.T("History.Resumed");
  if(current.remaining_percent>previous.remaining_percent)return L.T("History.IncreaseUnknown");
  if(!TrendView.Bridge(previous,current))return L.T("History.ContextBoundary");
  return L.T("History.Observed");
 }
 object SortValue(UsageSample row)=>sortColumn switch{
  1=>row.used_percent_raw??-1,2=>row.remaining_percent??-1,3=>previousById.GetValueOrDefault(row.sample_id) is UsageSample p&&DailyUsageCalculator.Reject(p,row).Length==0?row.remaining_percent-p.remaining_percent??decimal.MinValue:decimal.MinValue,
  4=>RowStatus(previousById.GetValueOrDefault(row.sample_id),row),_=>row.observed_at_utc
 };
 void RenderGrid(){
  if(grid.Columns.Count==0)return;
  string? selected=(grid.CurrentRow?.Tag as UsageSample)?.sample_id;int scroll=grid.FirstDisplayedScrollingRowIndex;
  var sorted=sortAscending?visibleRows.OrderBy(SortValue):visibleRows.OrderByDescending(SortValue);
  grid.SuspendLayout();grid.Rows.Clear();
  foreach(var c in sorted.Skip(page*PageSize).Take(PageSize)){
   var p=previousById.GetValueOrDefault(c.sample_id);
   var cycle=trend.GoldEvents.FirstOrDefault(x=>x.after_sample_id==c.sample_id&&x.Full);
   var idx=grid.Rows.Add(TrendView.Local(c.observed_at_utc,"yyyy-MM-dd HH:mm:ss"),Percent(c.used_percent_raw),Percent(c.remaining_percent),cycle!=null?L.T("History.RemainingChange","+"+TrendView.Number(cycle.delta_pp??0)):Change(p,c),RowStatus(p,c));
   var row=grid.Rows[idx];row.Tag=c;row.Cells[3].ToolTipText=L.T("History.ChangeTooltip");row.Cells[4].ToolTipText=RowStatus(p,c);
   if(c.sample_id==selected)grid.CurrentCell=row.Cells[0];
  }
  if(scroll>=0&&scroll<grid.Rows.Count)grid.FirstDisplayedScrollingRowIndex=scroll;
  grid.ResumeLayout();previous.Enabled=!busy&&page>0;next.Enabled=!busy&&(page+1)*PageSize<visibleRows.Count;
  if(!busy)SetStatus(service.History.SaveError==null?"PageStatus":"PageStatusUnsaved",visibleRows.Count==0?0:page+1,Math.Max(1,(visibleRows.Count+PageSize-1)/PageSize),visibleRows.Count);
 }
 void UpdateDetail(){
  if(grid.CurrentRow?.Tag is UsageSample selected)trend.SelectGoldSample(selected.sample_id);
  if(grid.CurrentRow?.Tag is not UsageSample c){detail.Text=L.T("History.SelectDetail");eventFocus.Enabled=false;return;}
  var p=previousById.GetValueOrDefault(c.sample_id);
  string qualityText=L.T(c.data_quality=="VALID"?"History.ValidData":"History.SourceIssue");
  string assurance=L.T(c.context_assurance=="STABLE_SCOPE"?"History.ScopeVerified":"History.ScopeLimited");
  detail.Text=L.T("History.DetailText",TrendView.Local(c.observed_at_utc,"yyyy-MM-dd HH:mm:ss zzz"),c.limit_id,c.window_duration_mins?.ToString(CultureInfo.InvariantCulture)??"—",RowStatus(p,c),assurance,qualityText);
  eventFocus.Enabled=FindEvent(c.sample_id)!=null;if(eventView.Visible)ShowEventFocus(FindEvent(c.sample_id));
 }

 UsageEvent? FindEvent(string sampleId)=>EventFocusEvidence.FocusEvents(events.Concat(chartFocusEvents).Where(x=>x.current_sample_id==sampleId)).FirstOrDefault();
 void OpenSelectedEventFocus(){if(grid.CurrentRow?.Tag is not UsageSample sample||FindEvent(sample.sample_id) is not {} found){SetStatus("NoEventForFocus");return;}ShowEventFocus(found);}
 void ShowEventFocus(UsageEvent? entry){eventView.Evidence=entry==null?null:EventFocusEvidence.Build(entry,chartFocusRows.Any(x=>x.sample_id==entry.current_sample_id)?chartFocusRows:rows,service.Radar.Snapshot(),DateTimeOffset.UtcNow);eventFocus.Enabled=entry!=null;eventView.Visible=true;trend.Visible=false;goldRail.Visible=false;if(recordsSection!=null)recordsSection.Visible=false;if(historyDivider!=null)historyDivider.Visible=false;UpdateModeButtons();if(IsHandleCreated)BeginInvoke((Action)(()=>{if(!IsDisposed&&eventView.Visible){Body.PerformLayout();var point=Body.PointToClient(eventView.PointToScreen(Point.Empty));Body.AutoScrollPosition=new Point(0,-Body.AutoScrollPosition.Y+point.Y-10);}}));}
 internal bool OpenEventFocusForSample(string sampleId){var found=FindEvent(sampleId);if(found==null)return false;ShowEventFocus(found);return true;}
 public async Task ExportCsv(){
  var filter=Filter();SetStatus("ExportingCsv");
  try{LastExport=await Task.Run(()=>ReportExporter.ExportCsv(service,filter));if(!IsDisposed)SetStatus("CsvDone",Path.GetFileName(LastExport));}
  catch{if(!IsDisposed)SetStatus("ExportFailed");}
 }
 void Note(){
  if(grid.CurrentRow?.Tag is not UsageSample sample)return;
  var matches=events.Where(x=>x.current_sample_id==sample.sample_id).ToList();
  if(matches.Count==0){SetStatus("NoEventForNote");return;}
  var entry=matches[0];string path=Path.Combine(service.Root,"event_notes.json");var notes=AtomicJson.Load(path,()=>new Dictionary<string,EventNote>());
  using var form=new ProductForm(L.T("History.NoteTitle"),520,330);
  var text=new TextBox{Dock=DockStyle.Fill,Multiline=true,MaxLength=2000,Text=notes.GetValueOrDefault(entry.event_id)?.Text??""};Theme.Style(text);form.Controls.Add(text);text.BringToFront();
  var save=Theme.Button(L.T("History.SaveNote"),(_,_)=>{try{notes[entry.event_id]=new(text.Text,entry.event_type,entry.observation_from_utc,entry.observation_to_utc);AtomicJson.Save(path,notes);form.Close();}catch{SetStatus("NoteFailed");}},true);
  save.Dock=DockStyle.Bottom;form.Controls.Add(save);save.BringToFront();
  L.Watch(form,()=>{form.Text=form.AccessibleName=L.T("History.NoteTitle");text.AccessibleName=L.T("History.NoteBody");save.Text=save.AccessibleName=L.T("History.SaveNote");});
  form.KeyPreview=true;form.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){e.Handled=true;form.Close();}};form.ShowDialog(this);
 }
 public string? LastExport{get;private set;}
 internal record EventNote(string Text,string EventType,DateTimeOffset From,DateTimeOffset To);
}

