"""Negative-path host checks; not model autonomy or live-Pro evidence."""
import argparse,contextlib,io,json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import run

class HostChecks(unittest.TestCase):
    def execute(self,responses,rpc_handler=None,max_turns=5,resume_state=None):
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
                if method=='tools/list':return {'tools':[{'name':name,'description':'test read','inputSchema':{'type':'object','properties':{'processId':{'type':'integer'}},'required':['processId']}} for name in ['get_active_map','get_analysis_job']]}
                if rpc_handler is not None:return rpc_handler(method,arguments)
                raise AssertionError('Unexpected GIS execution')
            def close(self):pass
        with tempfile.TemporaryDirectory(prefix='gis-agent-host-check-') as folder:
            resume=None
            if resume_state is not None:
                resume=str(Path(folder)/'prior.json')
                Path(resume).write_text(json.dumps(resume_state),encoding='utf-8')
            with patch.object(run,'ROOT',Path(folder)),patch.object(run,'settings',return_value={'model':'synthetic','provider':'test','endpoint':'https://example.invalid'}),patch.object(run,'Model',Model),patch.object(run,'Mcp',Mcp),contextlib.redirect_stdout(io.StringIO()):
                exit_code=run.run(argparse.Namespace(profile=None,pid=123,goal='测试目标',resume=resume,max_turns=max_turns))
            result=json.loads(next(Path(folder).rglob('run.json')).read_text(encoding='utf-8'))
            result['_exitCode']=exit_code
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

    @patch.object(run.time,'sleep')
    def test_long_job_wait_uses_one_model_decision(self,_sleep):
        polls=[]
        def rpc(method,arguments):
            self.assertEqual(arguments['name'],'get_analysis_job')
            polls.append(arguments)
            job={'jobId':'fixture-job','status':'completed' if len(polls)==101 else 'running','terminal':len(polls)==101}
            return {'content':[{'type':'text','text':json.dumps(job)}],'isError':False}
        report,_=self.execute([self.call('get_analysis_job',{'processId':123,'jobId':'fixture-job'}),self.call('finish_task',{'status':'partial','message':'已完成分析，未导出'},2)],rpc_handler=rpc,max_turns=2)
        self.assertEqual(len(polls),101)
        self.assertEqual(sum(e['kind']=='model' for e in report['events']),2)
        self.assertEqual(report['message'],'已完成分析，未导出')

    def test_job_error_not_retried(self):
        class Mcp:
            calls=0
            def rpc(self,*_):
                self.calls+=1
                return {'isError':True,'content':[{'type':'text','text':'fixture failure'}]}
        mcp=Mcp()
        self.assertTrue(run.wait_for_analysis(mcp,{'jobId':'fixture'},lambda _:None)['isError'])
        self.assertEqual(mcp.calls,1)

    def test_poll_timeout_preserves_uncertain_state(self):
        class Mcp:
            def rpc(self,*_):return {'content':[{'type':'text','text':json.dumps({'jobId':'fixture','terminal':False})}]}
        with self.assertRaisesRegex(RuntimeError,'不得重复提交'):
            run.wait_for_analysis(Mcp(),{'jobId':'fixture'},lambda _:None,timeout=0)

    def test_job_id_mismatch_stops(self):
        class Mcp:
            def rpc(self,*_):return {'content':[{'type':'text','text':json.dumps({'jobId':'other','terminal':True})}]}
        with self.assertRaisesRegex(RuntimeError,'ID不匹配'):
            run.wait_for_analysis(Mcp(),{'jobId':'fixture'},lambda _:None)

    def test_statistics_alias_is_scoped_and_conflicts_rejected(self):
        plan={'steps':[{'operation':'summary_statistics','parameters':{'group_field':'fclass','field':'osm_id','statistic':'COUNT'}},{'operation':'points_to_line','parameters':{'group_field':'road'}}]}
        run.canonicalize_plan(plan)
        self.assertEqual(plan['steps'][0]['parameters']['fields'],['fclass'])
        self.assertEqual(plan['steps'][1]['parameters']['group_field'],'road')
        with self.assertRaises(ValueError):run.canonicalize_plan({'steps':[{'operation':'summary_statistics','parameters':{'group_field':'a','fields':['b']}}]})

    def test_resume_reconciles_existing_job_without_resubmission(self):
        def rpc(method,arguments):
            self.assertEqual(arguments['name'],'get_analysis_job')
            return {'content':[{'type':'text','text':json.dumps({'jobId':'existing','terminal':True,'status':'completed'})}]}
        state={'processId':123,'messages':[{'role':'system','content':'old instructions'},{'role':'user','content':'prior goal'}],'pendingJobs':['existing'],'deliveries':[]}
        report,calls=self.execute([self.call('get_analysis_job',{'processId':123,'jobId':'existing'}),self.call('finish_task',{'status':'partial','message':'已核对原任务'},2)],rpc_handler=rpc,resume_state=state)
        self.assertEqual(report['message'],'已核对原任务')
        self.assertFalse(any(arguments.get('name')=='start_analysis' for _,arguments in calls))

    def test_resume_rejects_different_pro_process(self):
        with self.assertRaisesRegex(RuntimeError,'原Pro实例'):
            self.execute([],resume_state={'processId':999,'messages':[]})

    def test_display_edit_tracks_uri_after_layer_reordering(self):
        class Mcp:
            def rpc(self,*_):return {'content':[{'type':'text','text':json.dumps({'snapshotId':'new','inputs':[{'id':'L1','layerUri':'other'},{'id':'L9','layerUri':'target'}]})}]}
        old={'inputs':[{'id':'L1','layerUri':'target'}]}
        args={'processId':123,'mapUri':'map','snapshotId':'old','layerId':'L1','visible':False}
        actual,_=run.refresh_presentation_target(Mcp(),args,old)
        self.assertEqual(actual['layerId'],'L9')
        self.assertEqual(actual['snapshotId'],'new')
        self.assertEqual(args['layerId'],'L1')

    def test_display_edit_refuses_removed_target(self):
        class Mcp:
            def rpc(self,*_):return {'content':[{'type':'text','text':json.dumps({'snapshotId':'new','inputs':[]})}]}
        with self.assertRaisesRegex(ValueError,'未执行修改'):
            run.refresh_presentation_target(Mcp(),{'processId':123,'mapUri':'map','layerId':'L1'},{'inputs':[{'id':'L1','layerUri':'removed'}]})

    def test_crash_resume_marks_unanswered_calls_unknown_once(self):
        messages=[self.call('start_analysis',{},1)]
        run.reconcile_interrupted_messages(messages,['pending-job'])
        run.reconcile_interrupted_messages(messages,['pending-job'])
        replies=[message for message in messages if message['role']=='tool']
        self.assertEqual(len(replies),1)
        self.assertIn('结果未知',replies[0]['content'])
        self.assertIn('pending-job',replies[0]['content'])

    def test_continuous_raster_cannot_get_unique_value_style(self):
        for pixel in ['FLOAT','DOUBLE','']:
            with self.assertRaises(ValueError):run.validate_raster_style('classes',{'metadata':{'pixelType':pixel}})
        run.validate_raster_style('classes',{'metadata':{'pixelType':'SHORT'}})
        run.validate_raster_style('elevation',{'metadata':{'pixelType':'FLOAT'}})

    def test_sixty_decisions_pause_normally_with_explicit_reason(self):
        def rpc(method,arguments):
            return {'content':[{'type':'text','text':json.dumps({'mapUri':'fixture-map'})}]}
        responses=[self.call('get_active_map',{'processId':123},n) for n in range(60)]
        report,_=self.execute(responses,rpc_handler=rpc,max_turns=60)
        self.assertEqual(report['status'],'partial')
        self.assertEqual(report['stopReason'],'turn_limit')
        self.assertEqual(report['modelTurns'],60)
        self.assertEqual(report['maxTurns'],60)
        self.assertEqual(report['_exitCode'],0)
        self.assertIn('不是API账户',report['message'])

    def test_unlimited_task_passes_sixty_and_stops_on_agent_finish(self):
        def rpc(method,arguments):
            return {'content':[{'type':'text','text':json.dumps({'mapUri':'fixture-map'})}]}
        responses=[self.call('get_active_map',{'processId':123},n) for n in range(65)]
        responses.append(self.call('finish_task',{'status':'needs_input','message':'第66轮主动结束，等待用户补充'},66))
        report,_=self.execute(responses,rpc_handler=rpc,max_turns=0)
        self.assertEqual(report['status'],'needs_input')
        self.assertEqual(report['stopReason'],'')
        self.assertEqual(report['modelTurns'],66)
        self.assertEqual(report['maxTurns'],0)
        self.assertEqual(report['_exitCode'],0)

if __name__=='__main__':unittest.main()
