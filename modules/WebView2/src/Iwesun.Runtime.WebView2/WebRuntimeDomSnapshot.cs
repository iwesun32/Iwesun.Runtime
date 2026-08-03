using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

/// <summary>Captures the complete live DOM state exposed by a WebRuntime script session.</summary>
public static class WebRuntimeDomSnapshot
{
	/// <summary>Restores a snapshot and then applies data and event links without running the page's original business scripts.</summary>
	public static async Task<string> RestoreAndLinkAsync(
		IWebRuntimeScriptSession session,
		string snapshotJson,
		WebRuntimeDomLinkPlan linkPlan,
		CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(linkPlan);
		var restore = await RestoreAsync(session, snapshotJson, ct);
		var encodedPlan = JsonSerializer.Serialize(linkPlan, new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});
		var expression = LinkExpression.Replace("__LINK_PLAN__", encodedPlan, StringComparison.Ordinal);
		var links = NormalizeJsonResult(await session.EvaluateStringAsync(expression, ct));
		using var verification = JsonDocument.Parse(links);
		return JsonSerializer.Serialize(new
		{
			restored = JsonDocument.Parse(restore).RootElement.Clone(),
			links = verification.RootElement.Clone()
		});
	}

	/// <summary>Replaces the current document tree with an exact DOM snapshot while retaining the host's loaded resource environment.</summary>
	public static async Task<string> RestoreAsync(
		IWebRuntimeScriptSession session,
		string snapshotJson,
		CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
		ct.ThrowIfCancellationRequested();
		using (var document = JsonDocument.Parse(snapshotJson, new JsonDocumentOptions { MaxDepth = 4096 }))
		{
			if (!document.RootElement.TryGetProperty("top", out var top)
				|| !top.TryGetProperty("documentElement", out var documentElement)
				|| documentElement.ValueKind != JsonValueKind.Object)
			{
				throw new InvalidDataException("WebRuntime DOM snapshot does not contain a top document element.");
			}
		}
		var encodedSnapshot = JsonSerializer.Serialize(snapshotJson);
		var expression = RestoreExpression.Replace("__SNAPSHOT_JSON__", encodedSnapshot, StringComparison.Ordinal);
		var result = NormalizeJsonResult(await session.EvaluateStringAsync(expression, ct));
		if (string.IsNullOrWhiteSpace(result))
			throw new InvalidDataException("WebRuntime DOM restore returned an empty result.");
		using var verification = JsonDocument.Parse(result);
		if (!verification.RootElement.TryGetProperty("restored", out var restored) || !restored.GetBoolean())
			throw new InvalidDataException("WebRuntime DOM restore did not complete successfully.");
		var inlineFrameRestores = verification.RootElement.TryGetProperty("frameDocumentsRestored", out var inlineFrames)
			? inlineFrames.GetInt32()
			: 0;
		var frameRestores = new List<JsonElement>();
		using (var sourceDocument = JsonDocument.Parse(snapshotJson, new JsonDocumentOptions { MaxDepth = 4096 }))
		{
			var frameIndex = 0;
			foreach (var frameDocument in EnumerateFrameDocuments(sourceDocument.RootElement.GetProperty("top").GetProperty("documentElement")))
			{
				if (frameIndex < inlineFrameRestores)
				{
					frameIndex++;
					continue;
				}
				var url = frameDocument.TryGetProperty("url", out var urlElement) ? urlElement.GetString() ?? "" : "";
				var frameSnapshot = JsonSerializer.Serialize(new { top = frameDocument }, new JsonSerializerOptions { MaxDepth = 4096 });
				var frameExpression = RestoreExpression.Replace("__SNAPSHOT_JSON__", JsonSerializer.Serialize(frameSnapshot), StringComparison.Ordinal);
				var frameResult = NormalizeJsonResult(session is IWebRuntimeIndexedFrameScriptSession indexedSession
					? await indexedSession.EvaluateStringInFrameAsync(frameIndex, frameExpression, ct)
					: await session.EvaluateStringInFrameAsync(url, frameExpression, ct));
				using var frameVerification = JsonDocument.Parse(frameResult);
				if (!frameVerification.RootElement.TryGetProperty("restored", out var frameRestored) || !frameRestored.GetBoolean())
					throw new InvalidDataException($"WebRuntime frame DOM restore failed for '{url}'.");
				frameRestores.Add(frameVerification.RootElement.Clone());
				frameIndex++;
			}
		}
		return JsonSerializer.Serialize(new { restored = true, top = verification.RootElement.Clone(), frameRestores });
	}

	/// <summary>Captures all nodes, attributes, primitive runtime properties, open shadow roots, and accessible frame documents.</summary>
	public static async Task<string> CaptureAsync(
		IWebRuntimeScriptSession session,
		CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		ct.ThrowIfCancellationRequested();
		var json = NormalizeJsonResult(await session.EvaluateStringAsync(CaptureExpression, ct).ConfigureAwait(false));
		if (string.IsNullOrWhiteSpace(json))
			throw new InvalidDataException("WebRuntime DOM snapshot returned an empty result.");
		using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4096 });
		var root = document.RootElement;
		if (!root.TryGetProperty("schema", out var schema)
			|| schema.GetString() != "iwesun.webview2.dom-snapshot/1.0"
			|| !root.TryGetProperty("top", out var top)
			|| top.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidDataException("WebRuntime DOM snapshot result does not match the expected schema.");
		}
		return json;
	}

	private static string NormalizeJsonResult(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return value;
		using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 4096 });
		return document.RootElement.ValueKind == JsonValueKind.String
			? document.RootElement.GetString() ?? string.Empty
			: value;
	}

	private static IEnumerable<JsonElement> EnumerateFrameDocuments(JsonElement node)
	{
		if (node.ValueKind != JsonValueKind.Object)
			yield break;
		if (node.TryGetProperty("contentDocument", out var contentDocument)
			&& contentDocument.ValueKind == JsonValueKind.Object
			&& contentDocument.TryGetProperty("documentElement", out var frameRoot)
			&& frameRoot.ValueKind == JsonValueKind.Object)
		{
			yield return contentDocument.Clone();
			foreach (var nested in EnumerateFrameDocuments(frameRoot))
				yield return nested;
		}
		if (node.TryGetProperty("childNodes", out var children) && children.ValueKind == JsonValueKind.Array)
		{
			foreach (var child in children.EnumerateArray())
				foreach (var nested in EnumerateFrameDocuments(child))
					yield return nested;
		}
		if (node.TryGetProperty("shadowRoot", out var shadowRoot)
			&& shadowRoot.ValueKind == JsonValueKind.Object
			&& shadowRoot.TryGetProperty("childNodes", out var shadowChildren)
			&& shadowChildren.ValueKind == JsonValueKind.Array)
		{
			foreach (var child in shadowChildren.EnumerateArray())
				foreach (var nested in EnumerateFrameDocuments(child))
					yield return nested;
		}
	}

	/// <summary>JavaScript expression used by hosts that expose their own STA-bound evaluation adapter.</summary>
	public const string CaptureExpression = """
		JSON.stringify((()=>{
		  const primitive=value=>value===null||['string','number','boolean'].includes(typeof value);
		  const propertyNames=['value','checked','indeterminate','selected','selectedIndex','disabled','readOnly','required','multiple','open','hidden','scrollLeft','scrollTop','scrollWidth','scrollHeight','clientWidth','clientHeight','offsetWidth','offsetHeight','naturalWidth','naturalHeight','currentSrc','currentTime','duration','paused','contentEditable','isContentEditable','tabIndex','title','lang','dir','draggable','spellcheck'];
		  const documents=new WeakSet();
		  const segment=node=>{
		    if(node.nodeType===Node.TEXT_NODE){const peers=[...node.parentNode.childNodes].filter(item=>item.nodeType===Node.TEXT_NODE);return`text()[${peers.indexOf(node)+1}]`}
		    if(node.nodeType===Node.COMMENT_NODE){const peers=[...node.parentNode.childNodes].filter(item=>item.nodeType===Node.COMMENT_NODE);return`comment()[${peers.indexOf(node)+1}]`}
		    const tag=node.localName||node.nodeName.toLowerCase(),parent=node.parentElement;
		    if(!parent)return tag;
		    const peers=[...parent.children].filter(item=>item.localName===node.localName);
		    return peers.length>1?`${tag}[${peers.indexOf(node)+1}]`:tag;
		  };
		  const pathOf=node=>{const parts=[];for(let current=node;current&&current.nodeType!==Node.DOCUMENT_NODE;){parts.unshift(segment(current));current=current.parentNode instanceof ShadowRoot?current.parentNode.host:current.parentNode}return'/'+parts.join('/')};
		  const serializeNode=(node,index)=>{
		    const item={nodeType:node.nodeType,nodeName:node.nodeName,path:pathOf(node),childIndex:index};
		    if(node.nodeType===Node.TEXT_NODE||node.nodeType===Node.COMMENT_NODE){item.nodeValue=node.nodeValue??'';return item}
		    if(node.nodeType!==Node.ELEMENT_NODE)return item;
		    item.localName=node.localName;item.namespaceURI=node.namespaceURI;
		    item.attributes=[...node.attributes].map(attribute=>({name:attribute.name,localName:attribute.localName,namespaceURI:attribute.namespaceURI,prefix:attribute.prefix,value:attribute.value}));
		    const properties={};for(const name of propertyNames){try{if(name in node&&primitive(node[name]))properties[name]=node[name]}catch{}}
		    const ownPrimitiveProperties={};for(const name of Object.keys(node)){try{const value=node[name];if(primitive(value))ownPrimitiveProperties[name]=value}catch{}}
		    item.properties=properties;item.ownPrimitiveProperties=ownPrimitiveProperties;
		    if(node instanceof HTMLCanvasElement){try{item.canvasDataUrl=node.toDataURL()}catch(error){item.canvasError=String(error)}}
		    item.childNodes=[...node.childNodes].map((child,childIndex)=>serializeNode(child,childIndex));
		    if(node.shadowRoot)item.shadowRoot={mode:node.shadowRoot.mode,delegatesFocus:node.shadowRoot.delegatesFocus,serializable:node.shadowRoot.serializable,clonable:node.shadowRoot.clonable,childNodes:[...node.shadowRoot.childNodes].map((child,childIndex)=>serializeNode(child,childIndex))};
		    if(node instanceof HTMLIFrameElement){try{item.contentDocument=node.contentDocument?serializeDocument(node.contentDocument):null}catch(error){item.contentDocumentError=String(error)}}
		    return item;
		  };
		  const serializeDocument=doc=>{
		    if(documents.has(doc))return{url:doc.URL,cycle:true};documents.add(doc);
		    return{url:doc.URL,title:doc.title,baseURI:doc.baseURI,compatMode:doc.compatMode,characterSet:doc.characterSet,contentType:doc.contentType,readyState:doc.readyState,visibilityState:doc.visibilityState,documentElement:doc.documentElement?serializeNode(doc.documentElement,0):null};
		  };
		  return{schema:'iwesun.webview2.dom-snapshot/1.0',capturedAt:new Date().toISOString(),top:serializeDocument(document)};
		})())
		""";

	private const string RestoreExpression = """
		JSON.stringify((()=>{
		  const snapshot=JSON.parse(__SNAPSHOT_JSON__),source=snapshot.top?.documentElement;
		  if(!source)return{restored:false,error:'document-element-missing'};
		  let nodes=0,elements=0,attributes=0,properties=0,framesReused=0,documentsRestored=0,frameDocumentsRestored=0,shadowRootsRestored=0;
		  const restoreDocument=(sourceDocument,targetDocument,isFrame=false)=>{
		    if(!sourceDocument?.documentElement||!targetDocument?.documentElement)return false;
		    const existingFrames=[...targetDocument.querySelectorAll('iframe')],propertyQueue=[],shadowQueue=[],frameQueue=[],used=new Set();let frameIndex=0;
		    const createScript=()=>{const template=targetDocument.createElement('template');template.innerHTML='<script><\/script>';return template.content.firstChild};
		    const compatible=(node,item)=>!!node&&node.nodeType===item.nodeType&&(item.nodeType!==Node.ELEMENT_NODE||node.localName===item.localName);
		    const reconcile=(item,candidate=null)=>{
		      nodes++;
		      if(item.nodeType===Node.TEXT_NODE){const node=compatible(candidate,item)?candidate:targetDocument.createTextNode('');node.nodeValue=item.nodeValue??'';used.add(node);return node}
		      if(item.nodeType===Node.COMMENT_NODE){const node=compatible(candidate,item)?candidate:targetDocument.createComment('');node.nodeValue=item.nodeValue??'';used.add(node);return node}
		      if(item.nodeType!==Node.ELEMENT_NODE){const node=targetDocument.createTextNode('');used.add(node);return node}
		      elements++;let node,reusedFrame=false;
		      if(item.localName==='iframe'&&existingFrames[frameIndex]){node=existingFrames[frameIndex++];reusedFrame=true;framesReused++}
		      else if(compatible(candidate,item)){node=candidate}
		      else if(item.localName==='script')node=createScript();
		      else node=item.namespaceURI?targetDocument.createElementNS(item.namespaceURI,item.localName):targetDocument.createElement(item.localName);used.add(node);
		      const expected=new Set((item.attributes||[]).map(attribute=>attribute.name)),navigationAttributes=new Set(['src','srcdoc']);
		      for(const attribute of [...node.attributes])if(!expected.has(attribute.name)&&!(reusedFrame&&navigationAttributes.has(attribute.name)))node.removeAttribute(attribute.name);
		      for(const attribute of item.attributes||[]){try{if(reusedFrame&&navigationAttributes.has(attribute.name)&&node.hasAttribute(attribute.name)){attributes++;continue}attribute.namespaceURI?node.setAttributeNS(attribute.namespaceURI,attribute.name,attribute.value):node.setAttribute(attribute.name,attribute.value);attributes++}catch{}}
		      let cursor=node.firstChild;for(const childItem of item.childNodes||[]){let childCandidate=cursor;if(childItem.nodeType===Node.ELEMENT_NODE&&childItem.localName==='iframe')childCandidate=existingFrames[frameIndex]||null;else if(!compatible(childCandidate,childItem)||used.has(childCandidate))childCandidate=[...node.childNodes].find(child=>!used.has(child)&&compatible(child,childItem))||null;const child=reconcile(childItem,childCandidate);if(child!==cursor)node.insertBefore(child,cursor);cursor=child.nextSibling}
		      for(const child of [...node.childNodes])if(!used.has(child))child.remove();
		      propertyQueue.push([node,item.properties||{},item.ownPrimitiveProperties||{}]);
		      if(item.shadowRoot)shadowQueue.push([node,item.shadowRoot]);
		      if(item.localName==='iframe'&&item.contentDocument)frameQueue.push([node,item.contentDocument]);
		      return node;
		    };
		    const html=reconcile(sourceDocument.documentElement,targetDocument.documentElement);if(html!==targetDocument.documentElement)targetDocument.replaceChild(html,targetDocument.documentElement);documentsRestored++;if(isFrame)frameDocumentsRestored++;
		    for(const [node,values,ownValues] of propertyQueue){for(const [name,value] of Object.entries(values)){try{node[name]=value;properties++}catch{}}for(const [name,value] of Object.entries(ownValues)){try{node[name]=value;properties++}catch{}}}
		    for(const [host,shadow] of shadowQueue){try{const root=host.shadowRoot||host.attachShadow({mode:shadow.mode||'open',delegatesFocus:!!shadow.delegatesFocus,clonable:!!shadow.clonable,serializable:!!shadow.serializable});root.replaceChildren(...(shadow.childNodes||[]).map(item=>reconcile(item)));shadowRootsRestored++}catch{}}
		    for(const [frame,frameSource] of frameQueue){try{if(frame.contentDocument)restoreDocument(frameSource,frame.contentDocument,true)}catch{}}
		    return true;
		  };
		  const restored=restoreDocument(snapshot.top,document,false);
		  return{restored,nodes,elements,attributes,properties,framesReused,documentsRestored,frameDocumentsRestored,shadowRootsRestored,rootPath:'/html',rootChildren:document.documentElement.children.length};
		})())
		""";

	private const string LinkExpression = """
		JSON.stringify((()=>{
		  const plan=__LINK_PLAN__,byXPath=path=>{try{return document.evaluate(path,document,null,XPathResult.FIRST_ORDERED_NODE_TYPE,null).singleNodeValue}catch{return null}};
		  const result={dataLinked:0,eventsLinked:0,missing:[],invalid:[]};
		  for(const link of plan.dataLinks||[]){
		    const node=byXPath(link.xPath);if(!node){result.missing.push({kind:'data',xpath:link.xPath});continue}
		    try{
		      switch(String(link.operation||'link').toLowerCase()){
		        case'text':node.textContent=link.value==null?'':String(link.value);break;
		        case'attribute':if(!link.name)throw new Error('attribute name is required');link.value==null?node.removeAttribute(link.name):node.setAttribute(link.name,String(link.value));break;
		        case'property':if(!link.name)throw new Error('property name is required');node[link.name]=link.value;break;
		        case'hidden':node.hidden=!!link.value;break;
		        case'link':break;
		        default:throw new Error(`unsupported data operation: ${link.operation}`);
		      }
		      if(link.dataKey)node.dataset.runtimeDataKey=String(link.dataKey);node.dataset.runtimeDataLinked='true';result.dataLinked++;
		    }catch(error){result.invalid.push({kind:'data',xpath:link.xPath,error:String(error)})}
		  }
		  for(const link of plan.eventLinks||[]){
		    const node=byXPath(link.xPath);if(!node){result.missing.push({kind:'event',xpath:link.xPath});continue}
		    if(!link.eventId){result.invalid.push({kind:'event',xpath:link.xPath,error:'eventId is required'});continue}
		    const eventType=String(link.eventType||'click'),eventId=String(link.eventId);
		    node.dataset.runtimeEventId=eventId;node.dataset.runtimeEventType=eventType;
		    node.addEventListener(eventType,event=>{
		      if(link.preventDefault!==false)event.preventDefault();if(link.stopPropagation!==false)event.stopPropagation();
		      const message={source:'iwesun.runtime.webview2.dom-snapshot',kind:'event',eventId,eventType,xpath:link.xPath,value:'value'in node?node.value:null,checked:'checked'in node?!!node.checked:null};
		      try{globalThis.chrome?.webview?.postMessage(JSON.stringify(message))}catch{}
		      node.dispatchEvent(new CustomEvent('iwesun-runtime-snapshot-event',{detail:message,bubbles:true}));
		    },true);result.eventsLinked++;
		  }
		  document.documentElement.dataset.runtimeSnapshotLinks='true';return result;
		})())
		""";
}
