"""Prepare an isolated source-only fixture project; no analysis is executed here."""
import arcpy,argparse
from pathlib import Path
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--folder',default='outputs/autonomy-qa');args=parser.parse_args()
folder=root/args.folder
folder.mkdir(parents=True,exist_ok=True)
target=folder/'AutonomousTerrainQA.aprx'
if target.exists():
    raise RuntimeError('Preserve existing QA project; choose a new folder instead of overwriting.')
p=arcpy.mp.ArcGISProject(str(root/'outputs/analysis-qa/BridgeAnalysisQA.aprx'))
for m in p.listMaps():
    for layer in m.listLayers():
        if layer.name not in ('synthetic_dem.tif','synthetic_areas'):
            m.removeLayer(layer)
        else:
            layer.visible=True
p.homeFolder=str(folder)
p.saveACopy(str(target))
print(target)
