"""One-time native UI prefab authoring. No Unity import/Play hook calls this file.
Ordinary design work belongs in Prefab Mode; this script replaces sample UI assets.
"""
from pathlib import Path
import copy
import json
from unity_yaml import *

UI=json.loads((ROOT/'Tools/unity_ui_guids.json').read_text())
PREFABS=BASE/'Prefabs/UI'
PAPER=(.29,.28,.26,.82)
INK=(.94,.92,.87,1)
EDGE=(.68,.66,.59,.8)
DARK=(.14,.15,.15,.86)
LIGHT=(.42,.42,.39,.74)

class UIBuilder(Scene):
    def script_ui(self,node,name,**fields):
        return self.component(node,114,'MonoBehaviour',dict(m_Enabled=1,m_EditorHideFlags=0,m_Script=Flow(fileID=11500000,guid=UI[name],type=3),m_Name='',m_EditorClassIdentifier='',**fields))
    def text(self,name,parent,content,size,pos=(0,0),fontSize=13,tint=INK,align=3,anchor=(0,1),pivot=(0,1)):
        return super().text(name,parent,content,size,pos,fontSize,tint,align,anchor,pivot)
    def image_component(self,node):
        return next(i for c,i,k,v,st in self.docs if k=='MonoBehaviour' and v.get('m_GameObject',{}).get('fileID')==node['go'] and v.get('m_Script',{}).get('guid')==UI['Image'])
    def selectable(self, node, name, target, **fields):
        navigation=dict(m_Mode=3,m_WrapAround=0,m_SelectOnUp=ref(),m_SelectOnDown=ref(),m_SelectOnLeft=ref(),m_SelectOnRight=ref())
        colors=dict(m_NormalColor=color(1,1,1,1),m_HighlightedColor=color(.95,.95,.95,1),m_PressedColor=color(.72,.72,.7,1),m_SelectedColor=color(.96,.96,.96,1),m_DisabledColor=color(.55,.55,.55,.5),m_ColorMultiplier=1,m_FadeDuration=.1)
        return self.script_ui(node,name,m_Navigation=navigation,m_Transition=1,m_Colors=colors,m_Interactable=1,m_TargetGraphic=ref(target),**fields)
    def quantity_input(self,parent):
        node=self.ui('Quantity input',parent,(96,28),(91,-143),bg=DARK)
        _,text_id=self.text('Number',node,'1',(88,26),(4,-1),15,align=4)
        events={name:dict(m_PersistentCalls=dict(m_Calls=[])) for name in
                ('m_OnEndEdit','m_OnSubmit','m_OnSelect','m_OnDeselect','m_OnTextSelection','m_OnEndTextSelection','m_OnValueChanged','m_OnTouchScreenKeyboardStatusChanged')}
        return self.selectable(node,'TMP_InputField',self.image_component(node),m_TextViewport=ref(node['t']),
                               m_TextComponent=ref(text_id),m_Text='1',m_GlobalPointSize=15,m_CharacterLimit=10,
                               m_GlobalFontAsset=assetref(BASE/'Fonts/WorkerbornKorean-SDF.asset'),
                               m_ContentType=2,m_LineType=0,m_KeyboardType=4,m_CharacterValidation=2,
                               m_CaretBlinkRate=.85,m_CaretWidth=2,
                               m_SelectionColor=color(.29,.61,.85,.6),m_OnFocusSelectAll=1,m_ResetOnDeActivation=1,
                               m_RestoreOriginalTextOnEscape=1,m_RichText=0,**events)
    def quantity_slider(self,parent):
        node=self.ui('Quantity slider',parent,(278,22),(0,-177),bg=(.15,.16,.17,.45))
        track=self.ui('Track',node,(250,5),(14,0),(0,.5),(0,.5),(.13,.13,.13,.85))
        fill=self.ui('Fill',track,(0,0),(0,0),(0,0),(0,0),(.77,.68,.38,1))
        fill['tr'].update(m_AnchorMax=vec(0,1))
        handles=self.ui('Handle area',node,(250,22),(14,0),(0,.5),(0,.5))
        handle=self.ui('Handle',handles,(18,18),(0,0),(0,.5),(.5,.5),(.8,.76,.63,1))
        return self.selectable(node,'Slider',self.image_component(handle),m_FillRect=ref(fill['t']),
                               m_HandleRect=ref(handle['t']),m_Direction=0,m_MinValue=1,m_MaxValue=10,
                               m_WholeNumbers=1,m_Value=1,m_OnValueChanged=dict(m_PersistentCalls=dict(m_Calls=[])))
    def quantity_controls(self,parent):
        controls=dict(quantityInput=ref(self.quantity_input(parent)),quantitySlider=ref(self.quantity_slider(parent)))
        buttons=[('minimum','1'),('minusTen','-10'),('minusOne','-1'),('half','Half'),
                 ('plusOne','+1'),('plusTen','+10'),('maximum','Max')]
        for i,(field,label) in enumerate(buttons):
            _,button,_=self.button(label,parent,(37,26),(i*40,-202),label)
            controls[field]=ref(button)
        return controls
    def button(self,name,parent,size,pos,label=None):
        n=self.ui(name,parent,size,pos,bg=LIGHT)
        button=self.script_ui(n,'Button',m_Navigation=dict(m_Mode=0,m_WrapAround=0,m_SelectOnUp=ref(),m_SelectOnDown=ref(),m_SelectOnLeft=ref(),m_SelectOnRight=ref()),m_Transition=1,m_Colors=dict(m_NormalColor=color(1,1,1,1),m_HighlightedColor=color(1.18,1.15,1.08,1),m_PressedColor=color(.7,.7,.65,1),m_SelectedColor=color(1,1,1,1),m_DisabledColor=color(.5,.5,.5,.5),m_ColorMultiplier=1,m_FadeDuration=.08),m_SpriteState=dict(m_HighlightedSprite=ref(),m_PressedSprite=ref(),m_SelectedSprite=ref(),m_DisabledSprite=ref()),m_AnimationTriggers=dict(m_NormalTrigger='Normal',m_HighlightedTrigger='Highlighted',m_PressedTrigger='Pressed',m_SelectedTrigger='Selected',m_DisabledTrigger='Disabled'),m_Interactable=1,m_TargetGraphic=ref(self.image_component(n)),m_OnClick=dict(m_PersistentCalls=dict(m_Calls=[])))
        _,text=self.text('Label',n,label if label is not None else name,(size[0]-4,size[1]),(2,0),12,align=4)
        return n,button,text
    def raw(self,name,parent,size,pos=(0,0),tint=(1,1,1,1)):
        n=self.ui(name,parent,size,pos)
        i=self.graphic(n,UI['RawImage'],m_Material=ref(),m_Color=color(*tint),m_RaycastTarget=0,m_RaycastPadding=vec(0,0,0,0),m_Maskable=1,m_OnCullStateChanged=dict(m_PersistentCalls=dict(m_Calls=[])),m_Texture=ref(),m_UVRect=Flow(serializedVersion=2,x=0,y=0,width=1,height=1))
        return n,i
    def layout(self,n,horizontal=False,spacing=4):
        self.script_ui(n,'HorizontalLayoutGroup' if horizontal else 'VerticalLayoutGroup',m_Padding=dict(m_Left=0,m_Right=0,m_Top=0,m_Bottom=0),m_ChildAlignment=0,m_Spacing=spacing,m_ChildForceExpandWidth=1,m_ChildForceExpandHeight=0,m_ChildControlWidth=1,m_ChildControlHeight=1,m_ChildScaleWidth=0,m_ChildScaleHeight=0,m_ReverseArrangement=0)
    def element(self,n,w=-1,h=28):
        self.script_ui(n,'LayoutElement',m_IgnoreLayout=0,m_MinWidth=0,m_MinHeight=h,m_PreferredWidth=w,m_PreferredHeight=h,m_FlexibleWidth=0,m_FlexibleHeight=0,m_LayoutPriority=1)
    def grid(self,n,columns=8,cell=(32,32),spacing=(2,2)):
        return self.script_ui(n,'GridLayoutGroup',m_Padding=dict(m_Left=0,m_Right=0,m_Top=0,m_Bottom=0),m_ChildAlignment=0,m_CellSize=vec(*cell),m_Spacing=vec(*spacing),m_StartCorner=0,m_StartAxis=0,m_Constraint=1,m_ConstraintCount=columns)
    def scroll(self,name,parent,size,pos):
        viewport=self.ui(name,parent,size,pos,bg=(.1,.11,.11,.22))
        self.script_ui(viewport,'RectMask2D',m_Padding=vec(0,0,0,0),m_Softness=vec(0,0))
        content=self.ui('Content',viewport,size,(0,0))
        self.script_ui(viewport,'ScrollRect',m_Content=ref(content['t']),m_Horizontal=0,m_Vertical=1,m_MovementType=2,m_Elasticity=.1,m_Inertia=1,m_DecelerationRate=.135,m_ScrollSensitivity=28,m_Viewport=ref(viewport['t']),m_HorizontalScrollbar=ref(),m_VerticalScrollbar=ref(),m_HorizontalScrollbarVisibility=0,m_VerticalScrollbarVisibility=0,m_HorizontalScrollbarSpacing=0,m_VerticalScrollbarSpacing=0,m_OnValueChanged=dict(m_PersistentCalls=dict(m_Calls=[])))
        return viewport,content
    def bar(self,name,parent,size,pos,tint):
        n=self.ui(name,parent,size,pos,bg=tint);i=self.image_component(n)
        values=next(v for c,j,k,v,st in self.docs if j==i);values.update(m_Type=3,m_FillMethod=0,m_FillOrigin=0)
        return i
    def native_instance(self,parent,path,source_ids=None,size=None,pos=(0,0)):
        data=geo.documents(ROOT/path);root=next(d for d in data if 'RectTransform'in d and d['RectTransform']['m_Father']['fileID']==0)
        transform_id=root['_id'];modifications=[]
        for prop,value in [('m_AnchoredPosition.x',pos[0]),('m_AnchoredPosition.y',pos[1])]+([] if size is None else [('m_SizeDelta.x',size[0]),('m_SizeDelta.y',size[1])]):
            modifications.append(dict(target=assetref(path,transform_id),propertyPath=prop,value=str(value),objectReference=ref()))
        instance=self.add(1001,'PrefabInstance',dict(m_ObjectHideFlags=0,serializedVersion=2,m_Modification=dict(serializedVersion=3,m_TransformParent=ref(parent['t']),m_Modifications=modifications,m_RemovedComponents=[],m_RemovedGameObjects=[],m_AddedGameObjects=[],m_AddedComponents=[]),m_SourcePrefab=assetref(path,100100000)))
        t=self.add(224,'RectTransform',dict(m_CorrespondingSourceObject=assetref(path,transform_id),m_PrefabInstance=ref(instance),m_PrefabAsset=ref()),strip=True);parent['tr']['m_Children'].append(ref(t))
        ids={transform_id:t}
        for original_id in source_ids or []:
            source=next(d for d in data if d['_id']==original_id);kind=next(k for k in source if not k.startswith('_'))
            fields=dict(m_CorrespondingSourceObject=assetref(path,original_id),m_PrefabInstance=ref(instance),m_PrefabAsset=ref())
            if kind=='MonoBehaviour':fields.update(m_GameObject=ref(),m_Enabled=1,m_EditorHideFlags=0,m_Script=source[kind]['m_Script'],m_Name='',m_EditorClassIdentifier='')
            ids[original_id]=self.add(source['_class'],kind,fields,strip=True)
        return ids

results={}
def save(builder,name,root_component):
    path=PREFABS/(name+'.prefab');write(path,builder.docs);results[name]=(path,root_component);return assetref(path,root_component)
def source(name):return assetref(results[name][0],results[name][1])
def window(id,title,width,height,position=(0,0),paper=PAPER):
    b=UIBuilder();root=b.ui(title,None,(width,height),position,(.5,.5),(.5,.5),EDGE)
    inner=b.ui('Frame',root,(width-2,height-2),(1,-1),bg=paper)
    inner['tr'].update(m_AnchorMin=vec(0,0),m_AnchorMax=vec(1,1),m_AnchoredPosition=vec(0,0),m_SizeDelta=vec(-2,-2),m_Pivot=vec(.5,.5))
    header=b.ui('Title bar',root,(width-2,27),(1,-1),bg=(.3,.32,.32,.82))
    b.text('Title',header,title,(width-80,25),(8,-1),13)
    _,close,_=b.button('Close',header,(20,20),(width-26,-3),'×')
    _,mini,_=b.button('Minimize',header,(20,20),(width-49,-3),'-')
    drag=b.script(header,'Village/WindowDragHandle.cs')
    body=b.ui('Body',root,(width-18,height-43),(9,-34))
    fields=dict(windowId=id,closeButton=ref(close),dragHandle=ref(drag),minimizeButton=ref(mini),windowBody=ref(body['go']))
    return b,root,body,fields

def static_text(b,parent,title,pos,size,font=13):return b.text(title,parent,title,size,pos,font)[1]

def author():
    # Reusable authored widgets.
    b=UIBuilder();root=b.ui('Inventory cell',None,(32,32),bg=(.12,.14,.14,.77));save(b,'InventoryCell',root['t'])
    b=UIBuilder();root,button,label=b.button('Action',None,(152,27),(0,0),'선택');b.element(root,152,27)
    action=b.script(root,'UI/UiActionView.cs',label=ref(label),button=ref(button));save(b,'ActionButton',action)
    b=UIBuilder();root,button,_=b.button('Row',None,(420,66),(0,0),'');b.element(root,420,66)
    icon=b.ui('Icon',root,(46,46),(8,-8),bg=(1,1,1,1));icon_id=b.image_component(icon)
    _,title=b.text('Title',root,'이름',(330,26),(64,-5),13)
    _,detail=b.text('Details',root,'진행 상황',(330,24),(64,-30),12)
    progress=b.bar('Progress',root,(330,3),(64,-59),(.74,.57,.52,1))
    row=b.script(root,'UI/UiListRow.cs',title=ref(title),details=ref(detail),icon=ref(icon_id),progress=ref(progress),select=ref(button));save(b,'ListRow',row)
    b=UIBuilder();root,button,unused=b.button('Item',None,(32,32),(0,0),'');background=b.image_component(root)
    # Sample icon remains visible in Prefab Mode; data binding replaces it in Play.
    icon=b.ui('Icon',root,(28,28),(2,-2),bg=(1,1,1,1));icon['tr'].update(m_AnchorMin=vec(0,0),m_AnchorMax=vec(1,1),m_SizeDelta=vec(-4,-4),m_AnchoredPosition=vec(0,0),m_Pivot=vec(.5,.5))
    ic=b.image_component(icon);next(v for c,i,k,v,st in b.docs if i==ic)['m_RaycastTarget']=0;next(v for c,i,k,v,st in b.docs if i==ic).update(m_Sprite=assetref(BASE/'UI/Icons/potato.png'),m_PreserveAspect=1,m_RaycastTarget=0)
    _,count=b.text('Count',root,'',(28,18),(2,-14),10,align=5);ct=next(v for c,i,k,v,st in b.docs if i==count)
    ct['m_RaycastTarget']=0
    count_node=next(v for c,i,k,v,st in b.docs if i==next(v for c,i,k,v,st in b.docs if i==count)['m_GameObject']['fileID'])
    _,fallback=b.text('Fallback',root,'',(28,28),(2,-2),10,align=4)
    pointer=b.script(root,'Village/InventoryItemDrag.cs')
    item=b.script(root,'UI/UiItemView.cs',icon=ref(ic),count=ref(count),fallback=ref(fallback),background=ref(background),button=ref(button),pointer=ref(pointer));save(b,'ItemTile',item)
    b=UIBuilder();root=b.ui('Target link',None,(0,0),(0,0),(.5,.5),(.5,.5));line=b.ui('Line',root,(100,1.5),(0,0),(.5,.5),(0,.5),(1,.88,.36,1));line_id=b.image_component(line);next(v for c,i,k,v,st in b.docs if i==line_id).update(m_RaycastTarget=0,m_Material=assetref(BASE/'Materials/TargetLink.mat'))
    view=b.script(root,'UI/TargetLinkView.cs',line=ref(line_id),thickness=1.5);save(b,'TargetLink',view)

    b=UIBuilder();root=b.ui('Harvest skill',None,(68,68),(0,0),(0,0),(.5,0),(.12,.15,.16,.92))
    next(v for c,i,k,v,st in b.docs if i==b.image_component(root)).update(m_Sprite=assetref(BASE/'UI/Icons/SkillDisc.png'),m_RaycastTarget=0)
    n=b.ui('Skill icon',root,(40,40),(14,-14),bg=(1,1,1,1));icon=b.image_component(n)
    next(v for c,i,k,v,st in b.docs if i==icon).update(m_Sprite=assetref(BASE/'UI/Icons/hoe.png'),m_PreserveAspect=1,m_RaycastTarget=0)
    n=b.ui('Clockwise progress',root,(68,68),bg=(.86,.76,.38,1));progress=b.image_component(n)
    next(v for c,i,k,v,st in b.docs if i==progress).update(m_Sprite=assetref(BASE/'UI/Icons/SkillRing.png'),m_Type=3,m_FillMethod=4,m_FillOrigin=2,m_FillClockwise=1,m_FillAmount=0,m_RaycastTarget=0)
    view=b.script(root,'UI/SkillBalloonView.cs',icon=ref(icon),progress=ref(progress),worldHeight=2.25);save(b,'SkillBalloon',view)
    b=UIBuilder();root=b.ui('Cursor action',None,(136,28),(0,0),(0,0),(0,1))
    _,label=b.text('Action',root,'',(136,28),(0,0),13,align=0)
    view=b.script(root,'UI/WorldTooltipView.cs',label=ref(label),cursorOffset=vec(18,-22))
    save(b,'WorldTooltip',view)
    for name, script in (('WorldNameplate', 'WorldNameplateView'), ('PlayerOverhead', 'PlayerOverheadView'), ('VillageChat', 'VillageChatView')):
        path = PREFABS / f'{name}.prefab'
        script_guid = guid(BASE / 'Runtime/UI' / f'{script}.cs')
        component = next(d['_id'] for d in geo.documents(ROOT/path) if d.get('MonoBehaviour', {}).get('m_Script', {}).get('guid') == script_guid)
        results[name] = (path, component)
    b=UIBuilder();root,text=b.text('FloatingText',None,'',(260,42),(0,0),21,align=4,anchor=(0,0),pivot=(.5,0));save(b,'FloatingText',text)

    # Real window content is saved in each prefab, rather than reconstructed by a window factory.
    b,root,body,fields=window('inventory','소지품',588,424,(280,-25))
    static_text(b,body,'장비',(0,0),(220,25));static_text(b,body,'일반',(248,0),(272,25))
    equipment=b.ui('Equipment',body,(220,310),(0,-30),bg=(.12,.15,.17,.5));_,portrait=b.raw('Character portrait',equipment,(112,224),(54,-30))
    slots=[];icons=[]
    for title,slot,pos,size in [('머리',1,(174,-6),(38,38)),('몸',2,(172,-58),(42,78)),('손',3,(6,-30),(36,36)),('오른손',5,(6,-90),(38,110)),('왼손',6,(172,-157),(40,76)),('발',4,(88,-262),(50,38)),('장신구',7,(6,-262),(38,38))]:
        n=b.ui(title,equipment,size,pos,bg=(.09,.11,.13,.76));b.text(title,n,title,size,(0,0),10,tint=(.64,.65,.65,1),align=4)
        slots.append(ref(b.script(n,'Village/EquipmentSlotPointer.cs',slot=slot)));icon=b.ui('Equipped icon',n,size);icons.append(ref(icon['t']))
    viewport,content=b.scroll('Inventory viewport',body,(272,306),(248,-30));cells=b.ui('Cells',content,(272,136));grid=b.grid(cells)
    for i in range(32):
        c=b.ui('Cell '+str(i+1),cells,(32,32),(0,0),bg=(.09,.11,.12,.77));next(v for cl,j,k,v,st in b.docs if j==b.image_component(c))['m_RaycastTarget']=0
    items=b.ui('Items',content,(272,136),bg=(0,0,0,0));b.script(items,'Village/InventoryGridPointer.cs')
    static_text(b,body,'금화',(248,-344),(56,26));_,gold=b.text('Gold',body,'0',(204,26),(306,-344),13,align=5)
    _,bag=b.text('Bag',body,'일반',(220,24),(0,-343),12)
    view=b.script(root,'UI/InventoryWindowView.cs',**fields,scrollContent=ref(content['t']),itemLayer=ref(items['t']),cellLayer=ref(cells['t']),gridLayout=ref(grid),portrait=ref(portrait),gold=ref(gold),bagName=ref(bag),equipment=slots,equipmentIcons=icons);save(b,'InventoryWindow',view)

    b,root,body,fields=window('skills','스킬',478,505,(-280,10))
    rows=b.ui('Skill rows',body,(460,342));b.layout(rows,spacing=3)
    _,advance,advance_text=b.button('Advance',body,(142,28),(312,-350),'승급')
    _,use,_=b.button('Use',body,(126,28),(0,-350),'사용하기')
    _,ap=b.text('AP',body,'AP 0',(174,28),(134,-350),12)
    _,detail=b.text('Skill details',body,'',(450,72),(0,-385),13)
    view=b.script(root,'UI/SkillsWindowView.cs',**fields,skillRows=ref(rows['t']),advance=ref(advance),use=ref(use),advanceText=ref(advance_text),ap=ref(ap),details=ref(detail));save(b,'SkillsWindow',view)

    b,root,body,fields=window('quests','퀘스트',414,332,(-260,30),paper=(.64,.6,.5,.8))
    static_text(b,body,'일반',(0,0),(90,24))
    rows=[]
    for i in range(3):rows.append(ref(b.native_instance(body,results['ListRow'][0],[results['ListRow'][1]],size=(394,66),pos=(0,-32-i*74))[results['ListRow'][1]]))
    view=b.script(root,'UI/QuestWindowView.cs',**fields,firstWork=rows[0],expansion=rows[1],job=rows[2]);save(b,'QuestWindow',view)

    b,root,body,fields=window('questdetail','퀘스트 스크롤',638,386,(20,0),paper=(.82,.79,.67,.95))
    left=b.ui('Letter',body,(280,330),(0,0),bg=(.95,.94,.83,.9));right=b.ui('Objectives',body,(320,330),(292,0),bg=(.95,.94,.83,.9))
    _,description=b.text('Description',left,'퀘스트 내용',(260,176),(10,-8),13,tint=(.24,.2,.14,1));_,rewards=b.text('Rewards',left,'보상',(260,120),(10,-199),13,tint=(.24,.2,.14,1));_,objectives=b.text('Objectives',right,'목표',(298,290),(10,-8),13,tint=(.24,.2,.14,1))
    view=b.script(root,'UI/QuestDetailWindowView.cs',**fields,description=ref(description),rewards=ref(rewards),objectives=ref(objectives));save(b,'QuestScrollWindow',view)

    b,root,body,fields=window('character','캐릭터',442,405,(30,30))
    static_text(b,body,'기본 정보',(0,0),(120,25));_,identity=b.text('Identity',body,'일꾼\n1번째 생 · 1일째',(414,54),(0,-34),13)
    _,level=b.text('Level',body,'레벨 1 · 누적 1',(414,26),(0,-99),13)
    xp=b.bar('Experience',body,(410,4),(0,-129),(.82,.75,.44,1))
    _,stamina=b.text('Stamina',body,'스태미나 100/100',(414,26),(0,-145),13);energy=b.bar('Stamina fill',body,(410,6),(0,-174),(.71,.7,.3,1))
    _,ap=b.text('AP',body,'어빌리티 포인트 0',(414,26),(0,-191),13)
    _,statistics=b.text('Statistics',body,'수확 경력 0회\n아르바이트 0회\n작업 속도 ×1.00\n소지금 0 Gold\n마을 농장 1단계',(414,115),(0,-233),13)
    view=b.script(root,'UI/CharacterWindowView.cs',**fields,identity=ref(identity),level=ref(level),stamina=ref(stamina),ap=ref(ap),staminaBar=ref(energy),experienceBar=ref(xp),statistics=ref(statistics));save(b,'CharacterWindow',view)

    def map_view(b,parent,size,pos):
        n=b.ui('Village map',parent,size,pos,bg=(.67,.58,.42,1));image_node,image=b.raw('Map image',n,size,tint=(1,.9,.72,1))
        player=b.ui('Player marker',n,(5,5),(0,0),(0,0),(.5,.5),(1,1,.85,1));next(v for c,i,k,v,st in b.docs if i==b.image_component(player))['m_RaycastTarget']=0
        marker,marker_text=b.text('NPC marker template',n,'',(92,22),(0,0),11,INK,4,(0,0),(.5,.5));marker['g']['m_IsActive']=0
        b.script(n,'Village/VillageMapClick.cs')
        view=b.script(n,'UI/VillageMapView.cs',map=ref(image),player=ref(player['t']),npcMarker=ref(marker_text));return n,view
    b,root,body,fields=window('map','솔바람 마을',598,528,(0,0),paper=(.76,.68,.51,.92))
    n,map_component=map_view(b,body,(572,464),(0,0));view=b.script(root,'UI/MapWindowView.cs',**fields,map=ref(map_component));save(b,'MapWindow',view)

    b,root,body,fields=window('system','메뉴',240,160)
    _,save_button,_=b.button('Save',body,(218,30),(0,0),'저장');_,character,_=b.button('Character',body,(218,30),(0,-42),'캐릭터')
    view=b.script(root,'UI/SystemWindowView.cs',**fields,save=ref(save_button),character=ref(character));save(b,'SystemWindow',view)

    b,root,body,fields=window('tooltip','아이템 정보',294,165)
    _,title=b.text('Name',body,'아이템',(272,28),(0,0),14);_,description=b.text('Description',body,'설명',(272,84),(0,-34),12)
    view=b.script(root,'UI/ItemTooltipView.cs',**fields,title=ref(title),description=ref(description))
    root['tr']['m_Pivot']=vec(0,1)
    for c,i,k,v,st in b.docs:
        if 'm_RaycastTarget'in v:v['m_RaycastTarget']=0
    save(b,'ItemTooltip',view)
    b,root,body,fields=window('itemcontext','아이템',158,150)
    _,use,use_text=b.button('Use',body,(136,28),(0,0),'사용');_,split,_=b.button('Split',body,(136,28),(0,-38),'나누기')
    root['tr']['m_Pivot']=vec(0,1)
    view=b.script(root,'UI/ItemContextView.cs',**fields,use=ref(use),useLabel=ref(use_text),split=ref(split));save(b,'ItemContext',view)

    b,root,body,fields=window('shop','상점',376,367,(-230,145))
    static_text(b,body,'물품',(0,0),(340,24))
    viewport,content=b.scroll('Stock viewport',body,(272,220),(40,-32));cells=b.ui('Grid',content,(272,272));grid=b.grid(cells)
    for i in range(64):b.ui('Cell '+str(i+1),cells,(32,32),bg=(.09,.11,.12,.68))
    stock=b.ui('Stock items',content,(272,272))
    _,description=b.text('Selected item',body,'',(340,24),(0,-265),12)
    _,funds=b.text('Funds',body,'소지금 0 Gold',(340,24),(0,-294),12)
    sell_component=b.script(viewport,'UI/ShopSellTarget.cs')
    view=b.script(root,'UI/ShopWindowView.cs',**fields,stock=ref(stock['t']),cells=ref(grid),description=ref(description),funds=ref(funds),sellTarget=ref(sell_component));save(b,'ShopWindow',view)
    b,root,body,fields=window('quantity','아이템 거래',302,338,(0,0))
    _,title=b.text('Item name',body,'아이템',(278,28),(0,0),15,align=4)
    item_ref=b.native_instance(body,results['ItemTile'][0],[results['ItemTile'][1]],size=(84,104),pos=(97,-32))[results['ItemTile'][1]]
    controls=b.quantity_controls(body)
    _,total=b.text('Total',body,'가격 0 Gold',(278,27),(0,-232),13,align=4)
    _,confirm,confirm_text=b.button('Confirm',body,(278,30),(0,-263),'구입')
    view=b.script(root,'UI/QuantityWindowView.cs',**fields,**controls,item=ref(item_ref),title=ref(title),total=ref(total),confirmText=ref(confirm_text),confirm=ref(confirm));save(b,'QuantityWindow',view)

    b,root,body,fields=window('conversation','대화',1088,312,(0,-238),paper=(.28,.21,.18,.92))
    _,speaker=b.text('Speaker',body,'NPC',(174,30),(0,0),18);_,occupation=b.text('Occupation',body,'',(174,24),(0,-35),12)
    _,portrait=b.raw('NPC portrait',body,(150,210),(6,-53))
    _,body_title=b.text('Topic',body,'일상',(650,30),(182,0),15)
    _,body_text=b.text('Dialogue',body,'',(650,180),(182,-38),14)
    actions=b.ui('Actions',body,(650,30),(182,-222));b.layout(actions,True)
    keywords=b.ui('Keywords',body,(206,222),(850,-4));b.layout(keywords,False,7)
    _,leave,_=b.button('Leave',body,(206,28),(850,-230),'대화 종료')
    view=b.script(root,'UI/ConversationWindowView.cs',**fields,speaker=ref(speaker),occupation=ref(occupation),bodyTitle=ref(body_title),body=ref(body_text),keywords=ref(keywords['t']),actions=ref(actions['t']),leave=ref(leave),portrait=ref(portrait));save(b,'ConversationWindow',view)

    window_names=['InventoryWindow','SkillsWindow','QuestWindow','QuestScrollWindow','CharacterWindow','MapWindow','SystemWindow','ItemTooltip','ItemContext','ShopWindow','QuantityWindow','ConversationWindow']
    fields=dict(windows=[source(n) for n in window_names],item=source('ItemTile'),inventoryCell=source('InventoryCell'),actionButton=source('ActionButton'),listRow=source('ListRow'),skillBalloon=source('SkillBalloon'),worldTooltip=source('WorldTooltip'),nameplate=source('WorldNameplate'),floatingText=source('FloatingText'),playerOverhead=source('PlayerOverhead'),chat=source('VillageChat'))
    write(BASE/'Data/VillageUI.asset',[(114,11400000,'MonoBehaviour',mono('UI/VillageUiLibrary.cs',name='VillageUI',**fields),False)])
    (ROOT/'Tools/ui-prefab-manifest.json').write_text(json.dumps({name:dict(path=str(path),component=identifier) for name,(path,identifier) in results.items()},indent=2)+'\n')
    print('Authored',len(results),'UI prefabs with complete editable layouts and a shared prefab library.')

if __name__=='__main__':
    author()
