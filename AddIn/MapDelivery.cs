using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
namespace ProCodex;
internal static class MapDelivery {
    // Text is plain content, never CIM markup supplied by the model.
    static string Plain(string value,int limit){if(string.IsNullOrWhiteSpace(value)||value.Length>limit||value.Contains('<')||value.Contains('>'))throw new InvalidOperationException("文字为空、过长或包含标记。");return value;}
    public static async Task<object> Export(string title,string sourceNote,int dpi,string? extentLayerUri){
        Plain(title,80);Plain(sourceNote,220);if(dpi<96||dpi>300)throw new InvalidOperationException("DPI须96–300。");
        string folder=Path.Combine(Project.Current.HomeFolderPath,"AgentMaps",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
        Directory.CreateDirectory(folder);string pdf=Path.Combine(folder,"map.pdf"),png=Path.Combine(folder,"map.png");
        var result=await QueuedTask.Run(()=>{
            var view=MapView.Active??throw new InvalidOperationException("请先打开二维地图。");var map=view.Map;
            var extent=string.IsNullOrEmpty(extentLayerUri)?view.Extent:map.GetLayersAsFlattenedList().Single(l=>l.URI==extentLayerUri).QueryExtent();
            // Style item names are localized. Empty search uses local installed items by type.
            var styles=Project.Current.GetItems<StyleProjectItem>().ToArray();
            var arrows=styles.SelectMany(s=>s.SearchNorthArrows("")).ToArray();var scales=styles.SelectMany(s=>s.SearchScaleBars("")).ToArray();
            if(arrows.Length==0||scales.Length==0){StyleHelper.AddStyle(Project.Current,"ArcGIS 2D");styles=Project.Current.GetItems<StyleProjectItem>().ToArray();arrows=styles.SelectMany(s=>s.SearchNorthArrows("")).ToArray();scales=styles.SelectMany(s=>s.SearchScaleBars("")).ToArray();}
            if(arrows.Length==0||scales.Length==0)throw new InvalidOperationException("缺少本机指北针或比例尺样式，未创建新布局。");
            var layout=LayoutFactory.Instance.CreateLayout(new CIMPage{Width=11.69,Height=8.27,Units=LinearUnit.Inches});layout.SetName(title+" "+DateTime.Now.ToString("HHmmss"));
            var frame=ElementFactory.Instance.CreateMapFrameElement(layout,EnvelopeBuilderEx.CreateEnvelope(0.45,1.35,8.65,7.25),map);frame.SetName("Agent Map");frame.SetCamera(extent);
            void Text(double x,double y,string value,double size,string name)=>ElementFactory.Instance.CreateTextGraphicElement(layout,TextType.PointText,MapPointBuilderEx.CreateMapPoint(x,y),SymbolFactory.Instance.ConstructTextSymbol(ColorFactory.Instance.BlackRGB,size,"Microsoft YaHei","Regular"),value,name);
            Text(0.45,7.65,title,22,"Title");
            ElementFactory.Instance.CreateTextGraphicElement(layout,TextType.RectangleParagraph,EnvelopeBuilderEx.CreateEnvelope(0.45,0.1,11.25,0.7),SymbolFactory.Instance.ConstructTextSymbol(ColorFactory.Instance.BlackRGB,8,"Microsoft YaHei","Regular"),sourceNote,"Source");
            Text(0.45,0.78,"CRS: "+map.SpatialReference.Name+" | "+DateTime.Now.ToString("yyyy-MM-dd"),8,"CRS and date");
            var legend=ElementFactory.Instance.CreateMapSurroundElement(layout,EnvelopeBuilderEx.CreateEnvelope(8.95,1.2,11.25,5.5),new LegendInfo{MapFrameName=frame.Name},"Legend");
            CIMSymbolReference LegendText(double size)=>SymbolFactory.Instance.ConstructTextSymbol(ColorFactory.Instance.BlackRGB,size,"Microsoft YaHei","Regular").MakeSymbolReference();
            var legendDef=(CIMLegend)legend.GetDefinition();legendDef.AutoVisibility=true;legendDef.AutoFonts=false;legendDef.Title="图例";legendDef.ShowTitle=true;legendDef.TitleSymbol=LegendText(12);
            // Pro 3.0 populates automatic items incrementally. Snapshotting immediately can
            // retain just the first layer; explicitly bind one native item per visible leaf.
            var template=legendDef.Items.FirstOrDefault()??legendDef.DefaultLegendItem??new CIMHorizontalLegendItem();
            legendDef.Items=map.GetLayersAsFlattenedList().Where(l=>l.IsVisible&&l is not GroupLayer).Select(l=>{var item=(CIMLegendItem)CIMObject.Clone(template);item.Layer=l.URI;item.Name=l.Name;if(l is RasterLayer){item.ShowLayerName=true;item.ShowHeading=false;}return item;}).ToArray();
            foreach(var item in legendDef.Items){item.LayerNameSymbol=LegendText(8);item.HeadingSymbol=LegendText(8);item.LabelSymbol=LegendText(8);item.DescriptionSymbol=LegendText(8);}legend.SetDefinition(legendDef);
            var arrow=ElementFactory.Instance.CreateMapSurroundElement(layout,MapPointBuilderEx.CreateMapPoint(10.05,6.6),new NorthArrowInfo{MapFrameName=frame.Name,NorthArrowStyleItem=arrows[0]},"North arrow");arrow.SetHeight(0.65);
            var scale=ElementFactory.Instance.CreateMapSurroundElement(layout,MapPointBuilderEx.CreateMapPoint(1,0.98),new ScaleBarInfo{MapFrameName=frame.Name,ScaleBarStyleItem=scales[0]},"Scale bar");scale.SetWidth(2.4);
            var scaleDef=(CIMScaleBar)scale.GetDefinition();scaleDef.Units=LinearUnit.Kilometers;scaleDef.UnitLabel="km";scaleDef.LabelSymbol=LegendText(8);scaleDef.UnitLabelSymbol=LegendText(8);scale.SetDefinition(scaleDef);
            // Flatten at the requested DPI to avoid visible seams between transparent
            // raster tiles in PDF viewers. The editable layout remains in the project.
            layout.Export(new PDFFormat{OutputFileName=pdf,Resolution=dpi,DoFullRasterization=true});layout.Export(new PNGFormat{OutputFileName=png,Resolution=dpi});
            return new{layoutName=layout.Name,mapUri=map.URI,visibleLayers=map.GetLayersAsFlattenedList().Where(l=>l.IsVisible).Select(l=>new{name=l.Name,uri=l.URI}).ToArray(),extent=new{xmin=extent.XMin,ymin=extent.YMin,xmax=extent.XMax,ymax=extent.YMax},title,sourceNote,dpi,pdf,png,visualReview="not_performed_by_model"};
        });
        if(!await FrameworkApplication.Current.Dispatcher.InvokeAsync(()=>Project.Current.SaveAsync()).Task.Unwrap())throw new InvalidOperationException("地图已导出但工程保存失败。");
        if(!File.Exists(pdf)||!File.Exists(png)||new FileInfo(pdf).Length==0||new FileInfo(png).Length==0)throw new IOException("导出文件为空。");
        File.WriteAllText(Path.Combine(folder,"delivery.json"),JsonSerializer.Serialize(result,PipeJson.Options));return result;
    }
}
