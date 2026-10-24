$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'agent\transport\bin\Release\net6.0\ModelTransport.dll'))
$method=$assembly.GetType('ModelCompatibility').GetMethod('Apply',[Reflection.BindingFlags]'Static,NonPublic')
function Adapt([string]$url,[string]$payload){
 $values=New-Object object[] 2
 $values[0]=[Uri]$url
 $values[1]=[string]$payload
 $method.Invoke($null,$values)
}
$official='https://open.bigmodel.cn/api/paas/v4/chat/completions'
$body='{"model":"glm-5.3-flash","thinking":{"type":"disabled"},"max_tokens":4096,"messages":[]}'
$actual=Adapt $official $body|ConvertFrom-Json
if($actual.thinking.type -ne 'enabled' -or $actual.reasoning_effort -ne 'low' -or $actual.max_tokens -ne 4096){throw 'GLM compatibility/output budget failed'}
if((Adapt 'https://api.deepseek.com/chat/completions' $body) -ne $body){throw 'Unrelated provider changed'}
$other=$body.Replace('glm-5.3-flash','glm-4.7-flash')
if((Adapt $official $other) -ne $other){throw 'Unrelated model changed'}
$effort=$body.Replace('"max_tokens":4096','"reasoning_effort":"high","max_tokens":4096')
if((Adapt $official $effort|ConvertFrom-Json).reasoning_effort -ne 'high'){throw 'Explicit reasoning effort was lost'}
'Passed: GLM always-thinking adaptation, output budget, unrelated provider/model preservation, explicit effort preservation.'

$flashx=$body.Replace('glm-5.3-flash','glm-5.3-flashx')
$x=Adapt $official $flashx|ConvertFrom-Json
if($x.thinking.type -ne 'enabled' -or $x.reasoning_effort -ne 'low' -or $x.max_tokens -ne 4096){throw 'FlashX always-thinking compatibility failed'}
$xHigh=Adapt $official ($flashx.Replace('"max_tokens":4096','"reasoning_effort":"high","max_tokens":4096'))|ConvertFrom-Json
if($xHigh.reasoning_effort -ne 'high'){throw 'FlashX explicit effort was lost'}
$errors=$assembly.GetType('ApiErrors').GetMethod('Describe',[Reflection.BindingFlags]'Static,NonPublic')
$quota=$errors.Invoke($null,@(429,'{"error":{"code":"1113","message":"No applicable resource package"}}','synthetic-test-secret'))|ConvertFrom-Json
if($quota.errorType -ne 'resource_exhausted' -or $quota.error -notmatch '适用资源包'){throw 'Resource exhaustion classification failed'}
'Passed: FlashX compatibility, explicit effort preservation, model-scoped resource exhaustion.'
