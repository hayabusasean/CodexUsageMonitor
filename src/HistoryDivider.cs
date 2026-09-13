namespace CodexUsageMonitor;
internal sealed class HistoryDivider:Control {
 readonly Control table,chart;int startY,startTable,startChart;bool dragging;
 internal HistoryDivider(Control table,Control chart){this.table=table;this.chart=chart;Height=12;Tag=12;Dock=DockStyle.Top;DoubleBuffered=true;Cursor=Cursors.HSplit;TabStop=true;BackColor=Theme.Canvas;AccessibleRole=AccessibleRole.Grip;L.Watch(this,()=>AccessibleName=GoldVisual.Copy("Resize records and chart; use Up or Down","調整表格與圖表高度；可使用上下方向鍵"));}
 protected override void OnPaint(PaintEventArgs e){R2.Under(this,e.Graphics);using var pen=new Pen(Focused?GoldVisual.Light:Theme.Brand,2);e.Graphics.DrawLine(pen,Width/2-22,Height/2,Width/2+22,Height/2);}
 void Adjust(int delta){float k=DeviceDpi/96f;int min=(int)(190*k),total=startTable+startChart;int height=Math.Clamp(startTable+delta,min,Math.Max(min,total-(int)(150*k)));table.Height=height;chart.Height=total-height;Parent?.PerformLayout();}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;startY=PointToScreen(e.Location).Y;startTable=table.Height;startChart=chart.Height;dragging=true;Capture=true;}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging)Adjust(PointToScreen(e.Location).Y-startY);}
 protected override void OnMouseUp(MouseEventArgs e){dragging=false;Capture=false;base.OnMouseUp(e);}
 protected override bool IsInputKey(Keys k)=>k is Keys.Up or Keys.Down||base.IsInputKey(k);
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode is Keys.Up or Keys.Down){startTable=table.Height;startChart=chart.Height;Adjust((e.KeyCode==Keys.Down?20:-20)*DeviceDpi/96);e.Handled=true;}base.OnKeyDown(e);}
}
