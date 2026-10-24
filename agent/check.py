"""Negative-path host checks; not model autonomy or live-Pro evidence."""
import argparse,contextlib,io,json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import run

class HostChecks(unittest.TestCase):
    def execute(self,responses):
        class Model:
            asks=0
            def __init__(self,_):pass
            def ask(self,*_):
                value=responses[self.asks];self.asks+=1
                if isinstance(value,Exception):raise value
                return value,{}
            def close(self):pass
        class Mcp:
            calls=[]
            def __init__(self):pass
            def rpc(self,method,arguments):
                self.calls.append((method,arguments))
                if method=='tools/list':return {'tools':[{'name':'get_active_map','description':'test read','inputSchema':{'type':'object','properties':{'processId':{'type':'integer'}},'required':['processId']}}]}
                raise AssertionError('Unexpected GIS execution')
            def close(self):pass
        with tempfile.TemporaryDirectory(prefix='gis-agent-host-check-') as folder:
            with patch.object(run,'ROOT',Path(folder)),patch.object(run,'settings',return_value={'model':'synthetic','provider':'test','endpoint':'https://example.invalid'}),patch.object(run,'Model',Model),patch.object(run,'Mcp',Mcp),contextlib.redirect_stdout(io.StringIO()):
                run.run(argparse.Namespace(profile=None,pid=123,goal='测试目标',resume=None,max_turns=5))
            result=json.loads(next(Path(folder).rglob('run.json')).read_text(encoding='utf-8'))
            return result,Mcp.calls
    def call(self,name,args,n=1):
        return {'role':'assistant','content':None,'tool_calls':[{'id':'call_'+str(n),'type':'function','function':{'name':name,'arguments':json.dumps(args)}}]}
    def test_wrong_instance_never_reaches_mcp(self):
        report,calls=self.execute([self.call('get_active_map',{'processId':999}),self.call('finish_task',{'status':'needs_input','message':'需确认目标'},2)])
        self.assertEqual([m for m,_ in calls],['tools/list'])
        self.assertTrue(next(e['result']['isError'] for e in report['events'] if e['kind']=='tool_result'))
    def test_completion_requires_export_receipt(self):
        report,_=self.execute([self.call('finish_task',{'status':'completed','message':'模型无依据声称完成'}),self.call('finish_task',{'status':'partial','message':'无实际导出，未完成'},2)])
        self.assertEqual(report['status'],'partial')
        self.assertTrue(next(e['result']['isError'] for e in report['events'] if e['kind']=='tool_result'))
    def test_network_failure_does_not_retry_or_execute(self):
        report,calls=self.execute([RuntimeError('synthetic network failure')])
        self.assertEqual(report['status'],'interrupted')
        self.assertEqual(report['events'],[])
        self.assertEqual(len(calls),1)

if __name__=='__main__':unittest.main()
