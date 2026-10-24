using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace ProCodex;
internal sealed class ProDrawing {
    readonly List<IDisposable> overlays=new();
    Envelope? latestBounds;string latestMap="";
    public static async Task<string> TargetAsync()=>await QueuedTask.Run(()=> {
        var view=MapView.Active??throw new InvalidOperationException("请先打开二维地图视图。");
        if(view.Map.MapType!=MapType.Map)throw new InvalidOperationException("首版仅支持二维地图，暂不支持三维场景。");
        return view.Map.URI;
    });
    public async Task<int> ExecuteAsync(DrawingPlan plan,string target) {
        // Clone the validated plan. UI/model objects cannot change execution after confirmation.
        var frozen=DrawingPlan.Parse(plan.ToJson());
        return await QueuedTask.Run(()=> {
            var view=MapView.Active??throw new InvalidOperationException("地图视图已关闭。");
            if(view.Map.URI!=target||view.Map.MapType!=MapType.Map)throw new InvalidOperationException("当前地图已改变，请重新生成并预览计划。");
            var created=new List<IDisposable>();
            var extents=new List<Envelope>();
            try {
                foreach(var d in frozen.Drawings) {
                    var points=d.Coordinates.Select(c=>MapPointBuilderEx.CreateMapPoint(c[0],c[1],SpatialReferences.WGS84)).ToList();
                    Geometry geometry=d.Type=="point"?points[0]:d.Type=="polyline"?PolylineBuilderEx.CreatePolyline(points,SpatialReferences.WGS84):PolygonBuilderEx.CreatePolygon(points,SpatialReferences.WGS84);
                    // Single exterior ring: normalize direction without moving vertices.
                    if(geometry is Polygon reversed&&reversed.Area<0)geometry=GeometryEngine.Instance.ReverseOrientation(reversed);
                    if(geometry is Polygon polygon&&!GeometryEngine.Instance.IsSimpleAsFeature(polygon))throw new InvalidOperationException("面存在自交等拓扑问题，请修改坐标。");
                    if(view.Map.SpatialReference==null||view.Map.SpatialReference.Wkid==0)throw new InvalidOperationException("地图坐标系未知，未绘制。");
                    geometry=GeometryEngine.Instance.Project(geometry,view.Map.SpatialReference);
                    if(geometry==null||geometry.IsEmpty)throw new InvalidOperationException("投影后图形为空，未绘制。");
                    extents.Add(geometry.Extent);
                    var color=ColorFactory.Instance.CreateRGBColor(int.Parse(d.Color.Substring(1,2),NumberStyles.HexNumber),int.Parse(d.Color.Substring(3,2),NumberStyles.HexNumber),int.Parse(d.Color.Substring(5,2),NumberStyles.HexNumber));
                    CIMSymbol symbol=d.Type=="point"?SymbolFactory.Instance.ConstructPointSymbol(color,d.Size,SimpleMarkerStyle.Circle):d.Type=="polyline"?SymbolFactory.Instance.ConstructLineSymbol(color,d.Size,SimpleLineStyle.Solid):SymbolFactory.Instance.ConstructPolygonSymbol(color,SimpleFillStyle.Solid,SymbolFactory.Instance.ConstructStroke(color,d.Size));
                    created.Add(view.AddOverlay(geometry,symbol.MakeSymbolReference()));
                }
                // Fit the actual projected geometries, not a reprojected geographic envelope.
                double xmin=extents.Min(e=>e.XMin),xmax=extents.Max(e=>e.XMax),ymin=extents.Min(e=>e.YMin),ymax=extents.Max(e=>e.YMax);
                double minimum=view.Map.SpatialReference.IsGeographic?.005:1000;
                double dx=Math.Max(minimum,(xmax-xmin)*.2),dy=Math.Max(minimum,(ymax-ymin)*.2);
                latestBounds=EnvelopeBuilderEx.CreateEnvelope(xmin-dx,ymin-dy,xmax+dx,ymax+dy,view.Map.SpatialReference);latestMap=target;
                overlays.AddRange(created);return created.Count;
            }catch {foreach(var item in created)item.Dispose();throw;}
        });
    }
    public async Task ClearAsync()=>await QueuedTask.Run(()=>{foreach(var item in overlays)item.Dispose();overlays.Clear();});
    public async Task FitAsync()=>await QueuedTask.Run(()=>{
        var view=MapView.Active??throw new InvalidOperationException("请打开绘图所在地图。");
        if(latestBounds==null||overlays.Count==0||view.Map.URI!=latestMap)throw new InvalidOperationException("当前地图没有可定位的本次绘图。");
        if(!view.ZoomTo(latestBounds,TimeSpan.Zero))throw new InvalidOperationException("定位被中断，请重试。");
    });
}
