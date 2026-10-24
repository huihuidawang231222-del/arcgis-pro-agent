"""Actual stdio MCP -> named pipe -> Pro SDK integration checks, no model call."""
import argparse,json,subprocess,sys,time,uuid
from pathlib import Path
parser=argparse.ArgumentParser();parser.add_argument('--pid',required=True,type=int);args=parser.parse_args()
sys.stdout.reconfigure(encoding='utf-8');sys.stderr.reconfigure(encoding='utf-8')
root=Path(__file__).resolve().parents[1];out=root/'outputs/analysis-qa';records=[]
process=subprocess.Popen([sys.executable,'-X','utf8',str(root/'mcp/server.py')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8')
def rpc(method,params):
 n=len(records)+1;process.stdin.write(json.dumps(dict(jsonrpc='2.0',id=n,method=method,params=params))+'\n');process.stdin.flush();reply=json.loads(process.stdout.readline());assert reply.get('id')==n and 'error' not in reply,reply;records.append(dict(method=method,params=params,result=reply['result']));return reply['result']
def call(name,params,fail=False):
 r=rpc('tools/call',dict(name=name,arguments=params));assert r.get('isError',False)==fail,r;return r['content'][0]['text'] if fail else json.loads(r['content'][0]['text'])
def wait(job):
 for attempt in range(180):
  status=call('get_analysis_job',dict(processId=args.pid,jobId=job['jobId']))
  if status['terminal']:
   assert status['status']=='completed',status;return status
  time.sleep(1)
 raise AssertionError('Job did not complete; do not silently rerun')
try:
 rpc('initialize',dict(protocolVersion='2025-03-26',capabilities={},clientInfo=dict(name='synthetic-gis-agent-harness',version='1')))
 tools=rpc('tools/list',{})['tools'];assert len(tools)==14
 current=call('get_active_map',dict(processId=args.pid));target=dict(processId=args.pid,mapUri=current['mapUri'])
 context=call('get_analysis_context',target);assert len(context['operations'])==39
 dem=next(i for i in context['inputs'] if i['name']=='synthetic_dem.tif');areas=next(i for i in context['inputs'] if i['name']=='synthetic_areas')
 meta=call('inspect_layer',dict(target,snapshotId=context['snapshotId'],layerId=dem['id']));assert meta['metadata']['width']==100 and float(meta['statistics']['MINIMUM'])==100,meta
 invalid=dict(summary='invalid SQL/code must be rejected',steps=[dict(id='s1',operation='shell',inputs=[dem['id']],parameters={})])
 call('start_analysis',dict(target,snapshotId=context['snapshotId'],requestId=str(uuid.uuid4()),plan=invalid),True)
 plan=dict(summary='Synthetic terrain preparation: masked DEM then hillshade',steps=[dict(id='s1',operation='raster_clip',inputs=[dem['id'],areas['id']],parameters={}),dict(id='s2',operation='hillshade',inputs=['s1'],parameters=dict(z_factor=1,azimuth=315,altitude=45))])
 request=dict(target,snapshotId=context['snapshotId'],requestId=str(uuid.uuid4()),plan=plan)
 job=call('start_analysis',request);duplicate=call('start_analysis',request);assert job['jobId']==duplicate['jobId']
 first=wait(job);assert len(first['result']['outputs'])==2
 context2=call('get_analysis_context',target)
 call('inspect_layer',dict(target,snapshotId=context['snapshotId'],layerId=dem['id']),True)
 hill=next(i for i in context2['inputs'] if i['layerUri']==first['result']['outputs']['s2']['layerUri']);clipped=next(i for i in context2['inputs'] if i['layerUri']==first['result']['outputs']['s1']['layerUri'])
 observed=call('inspect_layer',dict(target,snapshotId=context2['snapshotId'],layerId=hill['id']));assert float(observed['statistics']['MINIMUM'])>=0 and float(observed['statistics']['MAXIMUM'])<=255
 # Decide the next operation using actual newly observed outputs and current IDs.
 plan2=dict(summary='Synthetic next action after observing hillshade: derive contours from actual clipped DEM',steps=[dict(id='s1',operation='contour',inputs=[clipped['id']],parameters=dict(interval=5,base=0))])
 second=wait(call('start_analysis',dict(target,snapshotId=context2['snapshotId'],requestId=str(uuid.uuid4()),plan=plan2)))
 context3=call('get_analysis_context',target);clipped=next(i for i in context3['inputs'] if i['layerUri']==first['result']['outputs']['s1']['layerUri'])
 call('style_raster',dict(target,snapshotId=context3['snapshotId'],layerId=clipped['id'],style='elevation',opacity=100))
 context4=call('get_analysis_context',target);hill=next(i for i in context4['inputs'] if i['layerUri']==first['result']['outputs']['s2']['layerUri'])
 call('style_raster',dict(target,snapshotId=context4['snapshotId'],layerId=hill['id'],style='grayscale',opacity=35))
 keep={first['result']['outputs']['s1']['layerUri'],first['result']['outputs']['s2']['layerUri'],second['result']['outputs']['s1']['layerUri']}
 displaycontext=call('get_analysis_context',target)
 hide=[i['layerUri'] for i in displaycontext['inputs'] if i.get('visible') and i['layerUri'] not in keep]
 for uri in hide:
  displaycontext=call('get_analysis_context',target);entry=next(i for i in displaycontext['inputs'] if i['layerUri']==uri)
  call('set_layer_visibility',dict(target,snapshotId=displaycontext['snapshotId'],layerId=entry['id'],visible=False))
 context5=call('get_analysis_context',target);dem5=next(i for i in context5['inputs'] if i['name']=='synthetic_dem.tif')
 cancelplan=dict(summary='Synthetic cancellation lifecycle test, not production output',steps=[dict(id='s1',operation='resample',inputs=[dem5['id']],parameters=dict(cell_size=1,resampling='NEAREST'))])
 canceljob=call('start_analysis',dict(target,snapshotId=context5['snapshotId'],requestId=str(uuid.uuid4()),plan=cancelplan))
 call('cancel_analysis_job',dict(processId=args.pid,jobId=canceljob['jobId']))
 cancelled=None
 for _ in range(60):
  cancelled=call('get_analysis_job',dict(processId=args.pid,jobId=canceljob['jobId']))
  if cancelled['terminal']:break
  time.sleep(1)
 assert cancelled['status']=='cancelled',cancelled
 call('get_analysis_job',dict(processId=args.pid,jobId='unknown-test-job'),True)
 report=dict(passed=True,livePro=True,modelInvoked=False,cancellation=cancelled,
engine='deterministic synthetic MCP test client, not an autonomous model benchmark',records=records,first=first,second=second)
 (out/'mcp-analysis-live.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(dict(passed=True,calls=len(records),first=first['result'],second=second['result']),ensure_ascii=False))
except Exception as exc:
 (out/'mcp-analysis-failure.json').write_text(json.dumps(dict(passed=False,error=str(exc),records=records),ensure_ascii=False,indent=2),encoding='utf-8')
 raise
finally:
 process.terminate();process.wait(timeout=10)
