# WorkerBorn · 솔바람 마을

직접 일하며 생활 스킬과 경력을 쌓고, 환생을 거쳐 발전한 마을로 돌아오는 Unity 프로토타입입니다.

Unity **6000.3.5f2**, URP **17.3.0**, Input System **1.17.0**을 사용합니다. `Assets/Workerborn/Scenes/VillagePrototype.unity`를 열어 Play하거나 **Workerborn → Village Prototype → Play Village**를 선택하세요. 빌드의 첫 Scene도 동일합니다.

## Unity에서 직접 수정하기

마을은 Scene에 배치된 **완성된 프리팹 인스턴스**로 구성합니다. Play에서 마을이나 창 레이아웃을 새로 만들지 않습니다. Scene에서 위치·회전과 개별 값을 바꾸거나, Prefab Mode에서 공통 모양·Collider·UI 구성을 수정하세요.

| 수정 대상 | 에셋 |
| --- | --- |
| 소지품·스킬·퀘스트·캐릭터·지도·상점·대화 등 각 창 | `Assets/Workerborn/Prefabs/UI/*Window.prefab` |
| 퀘스트 스크롤·툴팁·아이템 메뉴 | `Prefabs/UI/QuestScrollWindow.prefab`, `ItemTooltip.prefab`, `ItemContext.prefab` |
| 시계·미니맵·하단 메뉴·월드 이름 레이어 | `Prefabs/UI/VillageHUD.prefab` |
| 아이템 칸·아이콘·목록 행·버튼·이름 박스·대상 연결선 | `Prefabs/UI/InventoryCell.prefab`, `ItemTile.prefab`, `ListRow.prefab`, `ActionButton.prefab`, `PickupName.prefab`, `TargetLink.prefab` |
| 창과 위젯 프리팹 연결 목록 | `Data/VillageUI.asset` |
| 주인공과 NPC 전체 | `Prefabs/Characters/Player.prefab`, `Mira.prefab`, `Noah.prefab`, `Lena.prefab`, `FateGuide.prefab` |
| 게시판·우물·몬스터 예제 | `Prefabs/Interaction/Board.prefab`, `Shrine.prefab`, `Monster.prefab` |
| 감자 작물 전체 | `Prefabs/Crops/Potato.prefab` |
| 공용 드롭 프리팹과 아이템별 외형 | `Prefabs/Items/GroundPickup.prefab`, `Prefabs/Items/*.prefab` |
| 건물·가로등·나무·소품과 충돌 영역 | `Prefabs/World/*.prefab` |
| NPC 설정·대화·일자리·퀘스트·거래 품목 | `Data/NPCs`, `Data/Dialogues`, `Data/Jobs`, `Data/Quests`, `Data/Items` |
| NPC와 주인공의 부위별 외형·기존 애니메이션 | `Data/Characters/*.asset`, `Data/Characters/Parts/*.asset` |
| 시간·휴식·성장·환생 기본값 | `Data/VillageSettings.asset` |
| 스킬 이름·아이콘·AP 비용·랭크별 효과 | `Data/Skills/*.asset` |
| 월드 이펙트 프리팹·사전 생성 수·지속 시간 | `Data/VillageEffects.asset` |
| 수확 횟수·작업 시간·드롭 수·바닥 아이템 프리팹 | `Data/Potato.asset` |
| 카툰 재질과 원본 재질의 대응 | `Data/ToonPalette.asset`, `Materials/Toon/*.mat` |
| 외곽선 Renderer Feature와 렌더 파이프라인 | `Rendering/PC_Renderer.asset`, `Mobile_Renderer.asset`, 각 `*_Pipeline.asset` |

표의 짧은 경로는 모두 `Assets/Workerborn/` 아래에 있습니다. UI 창에는 제목 표시줄뿐 아니라 실제 내부 TextMeshProUGUI·Image·Button·RawImage·ScrollRect와 참조가 들어 있습니다. 실행 중에는 값과 이벤트를 연결하고, 목록 행·격자 아이콘처럼 수량이 바뀌는 요소만 지정된 위젯 프리팹으로 복제합니다. 프리팹에서 정한 위치·색·폰트·투명도·버튼 구성을 창을 열 때 지우고 다시 만들지 않습니다. 소지품 셀 크기는 `InventoryWindow` 안의 GridLayoutGroup에서 조정합니다.

창 테두리, 버튼, 소지품 슬롯과 HUD는 Layer Lab **GUI Pro – CasualGame** 스프라이트를 사용합니다. `Prefabs/UI`의 각 Image에서 스프라이트와 틴트를 편집합니다.


마을은 `Terrains/TerrainMeadows.asset`을 연결한 Unity Terrain과 Scene의 스카이박스 위에 있습니다. Terrain 도구로 높이와 지형 페인팅을 편집할 수 있습니다. `VillageNavigation/Build Bounds`에서 마을의 길찾기 범위를 조절합니다. 현재 Scene에서 작동하는 범위가 바뀌면 농장 활성화와 선택 부지에 맞춰 길찾기 데이터를 계산합니다.

건물·나무·바위의 클릭 판정은 각 월드 프리팹의 **Pointer geometry** 아래에 저장한 원본 메시 기반 MeshCollider를 사용합니다. 이 계층은 `PointerGeometry` 레이어(11)에 있으며 물리 충돌은 하지 않습니다. `VillageObstacle` 레이어(8)의 이동 제한 박스는 마우스 레이캐스트에서 제외하므로 지붕 주변의 빈 공간을 가로막지 않습니다. 원본 모델을 수정했다면 Prefab Mode에서 `VillageMapProp` 컴포넌트의 컨텍스트 메뉴 **Rebuild Pointer Geometry**로 클릭 메시를 다시 저장할 수 있습니다. 직접 FBX로 배치된 대장간은 ModelImporter의 메시 콜라이더를 사용합니다.

재사용한 FlatKit 지형 재질은 Valley 데모의 어두운 틴트와 특정 높이용 그라데이션을 해제했습니다. 감자 잎·뿌리와 마을 식물은 원본 Shader Graph의 고유 이름으로 저장된 알베도·노멀 슬롯을 카툰 재질에 연결합니다. 잎은 알파 컷아웃과 양면 렌더링을 유지합니다.

수확 중에는 `Prefabs/UI/SkillBalloon.prefab`의 **원형 아이콘과 위에서 시계방향으로 채워지는 게이지**만 표시합니다. 아이콘·크기·색·높이는 프리팹에서 수정합니다. 이동 중 새 목적지를 클릭해도 걷기 애니메이션의 재생 위치를 초기화하지 않습니다.

NPC 설정 에셋에서는 외형을 펼쳐 머리·의상·눈·눈썹·입·양손 파츠를 고르고 3D 미리보기를 드래그·휠로 회전·확대할 수 있습니다. 주인공은 `Data/Characters/Player_Default.asset`을 같은 형식으로 사용합니다. 파츠가 비어 있으면 기본 프리팹의 부분을 유지하고, 스킨 메시의 본은 이름으로 연결합니다. 공통 파츠 에셋은 사용하는 외형에 공유되므로 개인 변형은 복제해서 사용하세요. 외형 변경과 새 프리팹 배치 시 캐릭터 외형은 **Edit 모드에서** 갱신합니다.

마을의 기본 외형은 Rukha93 **Modular Anime Character 2**를 사용하는 `Prefabs/Characters/Looks`에 있습니다. 같은 리그용 머리와 의상, 눈·눈썹·입 재질은 `Data/Characters/Parts/ModularAnime2`에서 고릅니다. 의상 텍스처의 RGB 채널별 색상은 `Materials/Characters`에서 수정합니다. 주인공의 이동 모션은 `CharacterDefinition`, 이동 속도와 줍기·놓기 거리는 Player 프리팹의 `HarvestWorker`에서 조정합니다. 옷이 덮는 피부는 각 의상 모델의 `CharacterGarment`에서 지정합니다.

감자와 달걀 채집에는 Kevin Iglesias의 `Gathering`, 벌목에는 `TreeChopping` 시작·반복·마무리 모션을 사용합니다. 각 채집 대상의 `CropDefinition`에서 `Data/Characters/Work`의 작업 모션을 골라 연결합니다. 동작 재생은 채집 진행률에 맞춰 작업 시간 안에서 끝납니다.

상호작용은 공통 `WorldInteractable`을 상속합니다.

- `VillageInteractable`: NPC·게시판·우물. NPC의 ScriptableObject로 대화·역할을 설정합니다.
- `HarvestableCrop`: 이동·수확. 작물 ScriptableObject로 횟수·시간·드롭을 설정합니다.
- `AnimalNpc`는 암탉의 이름, Ctrl 선택, 살펴보기와 달걀 채집을 담당합니다.
- 도끼 없이 벌목용 소나무를 클릭하면 한 번 때려 나뭇가지를 떨어뜨립니다. 도끼를 장착하면 좌클릭으로 벌목하고, 우클릭 메뉴에서 때리기를 고를 수 있습니다.
- `GroundPickup`은 수평 거리 1.5m 안에서 즉시 획득합니다. 빛이 플레이어에게 날아가면 도착 이펙트가 터집니다. 멀리 있는 물품은 먼저 접근하며, 범위 안의 물품 획득은 진행 중인 수확·이동을 취소하지 않습니다. 드롭은 공용 `GroundPickup.prefab`의 인스턴스를 재사용합니다. `Data/Items`에서 이름을 수정하고 해당 항목의 Ground Prefab에서 메시·재질·배치를 편집합니다. 그 프리팹은 Scene에 배치할 때도 사용할 수 있습니다.
- `MonsterInteractable`은 서쪽 벌목터 바깥의 숲토끼 전투와 갈무리를 담당합니다. 클릭마다 한 번 공격하고, 다음 공격은 공격 간격이 지난 뒤 다시 클릭합니다. 토끼는 첫 공격을 맞은 뒤 사거리 안의 플레이어를 자동으로 반격합니다.

공통 상호작용 거리·이름 위치·요청/완료 UnityEvent를 Inspector에서 설정할 수 있습니다. 새 유형은 `Interact`를 재정의하고 기존 `HarvestWorker` 명령을 연결합니다. 숲토끼의 체력·반격·전리품은 `Data/Combat/ForestRabbit.asset`, 플레이어 공격 간격과 모션은 Player 프리팹의 `HarvestWorker`에서 조정합니다. 토끼가 쓰러지는 순간 고기가 떨어지고, 토끼는 다음 날 돌아옵니다.

대상 우클릭 메뉴는 `Prefabs/UI/WorldContextMenu.prefab`에 있습니다. 공통 상호작용과 NPC가 제공하는 대화 주제를 표시합니다. 소지품에서는 커서가 가리키는 칸이 들고 있는 아이템의 좌상단이 됩니다. 어두운 배치 미리보기는 `InventoryWindow`의 `Placement preview`에서 조정합니다. 물품을 내려놓을 지점이 수평 1.5m 안에 들어오면 가방에서 빠져나옵니다.

노아와 대화 중 **구매하기**를 선택하면 상점과 소지품이 열립니다. 상점 물품을 클릭하면 구매할 수 있고, 소지품의 묶음을 상인의 물품 그리드에 놓으면 그 묶음에서 판매합니다. 구매·판매·**아이템 나누기**는 같은 `QuantityWindow.prefab`을 사용합니다. 수량은 슬라이더, 숫자 입력, `1 / -10 / -1 / Half / +1 / +10 / Max`로 조정합니다. 나누기는 소지품의 우클릭 메뉴에서 시작하며 원래 묶음에 한 개 이상 남깁니다. 노아에게서 가방을 사면 즉시 장착되어 소지품이 두 줄 늘어납니다.

작물과 Scene에 놓은 아이템은 고유 ID로 저장합니다. 복제하면 Editor에서 새 ID를 부여해 서로 다른 개체의 수확 상태가 공유되지 않게 합니다. 새 외형·NPC·아이템 등의 데이터는 **Create → Workerborn**에서 생성합니다. NPC 프리팹의 CharacterActor와 VillageInteractable에 같은 NPC 에셋을 연결하세요. 현재 농장 일자리는 한 종류와 그 경력을 공유합니다.

길찾기는 배치한 바닥 Collider와 `VillageNavigationBlocker` 상자로 계산합니다. 건물을 움직일 때 자식 장애물도 함께 움직입니다. `Village session/VillageNavigation` Inspector에서 경로를 베이크할 수 있으며, 비어 있으면 Play 시 경로 데이터만 계산합니다.

## 카툰 렌더링과 대상·아이템 표시

준비된 **FlatKit/Stylized Surface** 셰이더로 프로젝트 전용 카툰 재질을 만들었습니다. 원본 텍스처·색·UV·알파 컷아웃은 유지하고, 기존 FlatKit **ObjectOutlineRendererFeature**를 PC·모바일 Renderer에 연결했습니다. 원본 에셋팩의 재질은 수정하지 않습니다. 외형 미리보기와 새로 배치한 프리팹도 `ToonPalette` 매핑을 사용합니다. 새로운 원본 재질을 쓸 때는 카툰 재질을 직접 지정하거나 매핑에 추가하세요.

월드 메시와 Alt 이름 박스는 같은 `PointerTargeting`으로 대상을 찾습니다. 선택되면 메인 카메라에서 Flat Kit의 `Outline` 패스를 활성화한 재질의 외곽선 색과 굵기를 바꿉니다. 색은 `PointerTargeting`, 굵기는 `CameraVisibility`에서 조정합니다. **Alt가 눌려 있을 때만** 이름 박스의 배경·호버·누름 색과 클릭 영역이 활성화됩니다. 평소에는 원래 투영 위치에 텍스트만 표시합니다. 커서 우하단에는 “대화하기”, “수확하기”, “줍기” 같은 행동만 표시합니다. 수확이 끝난 작물은 자신의 Collider를 비활성화하므로 마우스 레이캐스트에서도 제외되며, 다음 날 수확 횟수를 초기화하면 다시 감지됩니다.

Ctrl을 누르면 화면에 보이는 NPC·몬스터 중 커서와 가장 가까운 대상이 선택됩니다. 호버와 클릭은 깊이·ID 마스크에서 화면에 보이는 메시 픽셀을 확인합니다. Ctrl 연결선은 대상의 **캡슐 중심점**을 향하다가 캐릭터 실루엣 앞에서 멈춥니다. Ctrl+클릭도 같은 상호작용 경로를 사용합니다. 선 색과 ID 읽기 빈도는 `Village session/PointerTargeting`, 선 굵기는 `TargetLink.prefab`에서 수정합니다. 비동기 GPU 읽기를 지원하지 않는 기기는 동기 읽기를 사용합니다.

입력은 **Input System의 `Input/Workerborn.inputactions`**에서 Gameplay·Camera·UI 맵으로 수정합니다. Scene의 `WorkerbornInput`과 EventSystem의 `InputSystemUIInputModule`이 이 에셋을 공유합니다. UI 입력 참조도 `Input/UI/*.asset`으로 연결돼 있습니다. 장치 API를 직접 조회하는 게임 코드는 액션 조회로 교체했습니다.

게임 내 텍스트는 TMP입니다. `Fonts/WorkerbornKorean-SDF.asset`은 포함된 한글 OTF를 사용하는 동적 SDF 폰트이며 여러 아틀라스를 지원합니다. 이름표·행동 안내의 검은 외곽선은 `Fonts/WorkerbornKorean-NameOutline.mat`, 일반 UI 외곽선은 폰트 에셋의 Material에서 조정합니다.

`Village session/VillageLighting`에서 아침과 저녁 전환 시간, 주변광과 가로등을 조정합니다. `Data/VillageEffects.asset`의 표에서 클릭·착지·획득 도착·레벨업·드롭 트레일·획득 트레일의 프리팹과 사전 생성 수를 선택합니다. 트레일의 지속 시간은 해당 프리팹의 Trail Renderer에서 편집합니다. 바닥 아이템의 사전 생성 수는 `Village session`의 Prewarm Ground Pickups에서 조정합니다. 드롭과 이름표, 이펙트는 사용이 끝나면 풀로 돌아가고 동시에 더 많이 필요하면 풀이 늘어납니다.

`Village Weather` 프리팹의 COZY 3가 게임 날짜와 시각을 받아 하늘과 날씨를 연출합니다. 하루의 날씨는 누적 일자로 결정되며, 자거나 환생해 날짜가 바뀌면 다음 날 날씨가 적용됩니다. 기본 비율은 맑음 80%, 구름 15%, 약한 비 5%입니다. 프로필·비율·시드는 프리팹의 `VillageWeather`, 하늘색은 `Data/Weather/VillageAtmosphere.asset`에서 조정합니다.

카메라는 지정한 줌 거리를 유지합니다. `Main camera/CameraVisibility`가 플레이어를 가리는 일반 배경의 렌더러를 메인 카메라에서만 숨깁니다. 플레이어의 두 시선 지점과 카메라 사이에 배경의 `Pointer geometry` 메시가 있을 때 페이드를 시작합니다. 갑자기 사라지는 대신 지정한 시간에 걸쳐 몸체와 외곽선이 함께 페이드됩니다. 재질은 처음부터 페이드를 지원하는 셰이더에 연결하며, 사라질 때 머티리얼·셰이더·텍스처를 교체하지 않습니다. `_CameraOpacity`만 메인 카메라의 MaterialPropertyBlock으로 적용하고 렌더 직후 복구합니다. 불투명 디더 방식으로 몸체·외곽선·깊이·그림자가 같은 기준으로 사라집니다. 숲은 제공된 Synty 그래프의 원래 텍스처·이끼·바람 노드를 유지합니다. NPC·작물·아이템이 플레이어를 가려도 숨기지 않으며, 캐릭터·아이템은 여유 공간을 확장한 캡슐과 모델의 전체 bounds, 카메라 near plane까지 함께 판정해 관통 전에 페이드를 시작합니다. 감자의 회전·부유, 머리카락·모자·장비도 판정에 포함됩니다. 숨긴 배경은 마우스 대상 감지에서 건너뛰고, 미니맵·초상화·충돌·내비게이션은 그대로 작동합니다. 캡슐 크기는 각 캐릭터·아이템 프리팹에서 수정합니다.

바닥 아이템과 NPC 이름은 평소에도 표시합니다. **Alt를 누르는 동안** 감자 등 상호작용 가능한 대상에 하늘색 이름과 짙은 반투명 박스가 나타납니다. 박스를 클릭하면 대상과 상호작용합니다. 겹친 박스는 화면에 투영한 대상 위치의 높이로 정렬합니다. 대상 높이가 같으면 **생성 순서대로 위→아래**에 놓입니다. 한 화면보다 큰 더미는 Alt를 누르고 이름 위에서 휠을 돌려 살펴볼 수 있습니다. 글꼴·여백·일반·호버·누름 색상과 투명도는 `PickupName.prefab`과 `NpcName.prefab`에서 조정합니다.

## 조작

| 입력 | 동작 |
| --- | --- |
| 좌클릭 | 이동, 작물 수확, 바닥 물품으로 접근·획득, NPC와 대화 |
| 우클릭 드래그 또는 휠 버튼 드래그 | 3인칭 자유 시점의 좌우·상하 회전 |
| 대상 위에서 짧은 우클릭 | 대화·채집·줍기 등 대상의 상호작용 메뉴 열기 |
| WASD / Shift | 시점 기준으로 이동 / 누르는 동안 걷기 |
| E / E 길게 | 주변의 가장 가까운 아이템 줍기 / 연속 줍기 |
| 마우스 휠 | 줌. 기본 거리 범위 1.8~22m |
| Ctrl / Ctrl+클릭 | 가까운 NPC·몬스터 선택과 연결선 / 해당 대상에게 접근 |
| Alt / Alt 이름 박스 클릭 | 상호작용 대상의 하늘색 이름 박스 표시 / 대상과 상호작용 |
| 빈 곳에서 짧은 우클릭 | 이동·미완료 작업 취소 |
| Esc | 열린 창을 닫거나 시스템 메뉴 열기 |
| I / Q / Z 또는 K / C | 소지품 / 퀘스트 / 스킬 / 캐릭터 |
| M | 미니맵 표시·숨김. 하단 지도 버튼은 큰 지도를 엽니다. |
| 소지품 아이템 좌클릭 | 물건을 집거나 계약서를 펼칩니다. 집은 물건은 다시 클릭해 놓습니다. |
| 소지품 아이템을 땅으로 드래그 | 놓을 위치의 수평 1.5m 안에서 내려놓기 |
| 들고 있는 물건 우클릭 / Esc | 원래 보관 자리로 복귀 |
| 아이템 우클릭 / Ctrl+클릭 | 사용·장착·나누기 메뉴 / 바로 사용·장착 |
| 왼쪽 장비 칸 클릭 | 들고 있는 장비 장착, 또는 장착 장비를 소지품으로 꺼내 들기 |
| 창 제목 표시줄 드래그 | 정보 창 이동 |
| 지도 클릭 | 해당 지점으로 이동 |

장비 칸은 소지품 왼쪽에 있습니다. 괭이는 **장착한 동안** 작업 효과를 줍니다. 골드는 한 칸에 1,000개씩 보관하며 거래·보상·기부는 실제 금화 수량을 바꿉니다. 소지품에 공간이 없으면 거래를 취소하고 수량을 보존합니다.

상시 HUD는 미니맵, 그 왼쪽의 요일과 시각, 레벨·스태미나·경험치, 하단 창 메뉴를 표시합니다.

UX의 기준은 마비노기의 [공식 Rookie Guide](https://mabibook.nexon.net/usa/book_rookie_guide.html)와 [Beginner Guide](https://nxcache.nexon.net/mabinogi/microsite/BeginnerGuide/)에 나오는 클릭 이동, 오른쪽 드래그 시점, 휠 줌, 클릭으로 집고 놓는 소지품, 장비 영역과 키워드 대화입니다.

## 두 생애를 시험하기

1. 새 게임은 구름 위 **운명의 인도자**와의 대화로 시작합니다. 마을에 내려가 **미라**에게 목재 납품 계약서를 받고, 소지품에서 펼쳐 보세요. 퀘스트창에도 농장 건설 목표와 납품 계약이 함께 표시됩니다.
2. 광장 가장자리의 **암탉**을 클릭해 달걀을 모으고, 떨어진 달걀을 주워 **노아**의 잡화점에 팝니다. 달걀은 개당 4G이며 **나무꾼 도끼**는 12G입니다. 도끼를 주무기에 장착하고 마을 서쪽의 소나무를 베어 **목재 6개**를 납품하세요.
3. 마을 옆 밭터나 남쪽 밭터의 팻말을 선택하고, **기억의 우물**에서 첫 환생을 진행하세요. 환생 뒤 고른 부지에 농가와 감자밭이 열립니다. 두 번째 삶에서도 소지품은 빈 상태로 시작합니다.
4. **08:00~18:00** 사이에 미라에게 농장 계약서를 받으세요. 감자 6개를 납품하면 45G와 경험치, 경력을 얻습니다. 같은 감자의 일반 판매가는 18G이며, 더 큰 계약은 누적 경력에 따라 열립니다.

클릭 한 번으로 작업이 시작됩니다. 달걀 채집 2초, 벌목 3.5초, 감자 수확 2.4초가 기본입니다. 실패하면 작업 기회와 스태미나가 소모됩니다. 채집 결과의 럭키 보너스는 2배, 해당 스킬의 지정 랭크에서 열리는 대박 보너스는 50배입니다. 보상은 여러 바닥 아이템으로 나뉘어 날아갑니다. 기본 표시 상한은 24개이며 큰 보너스는 각 아이템의 수량에 나누어 담습니다. 드롭 반경과 각도, 도구·성공률·시간·수확 횟수에 더해 적용할 스킬을 `Data/Gathering`과 `Data/Potato.asset`에서 조절합니다.

수확·벌목·갈무리·맨손전투·달걀 채집은 각각 연습 랭크에서 시작해 AP로 랭크 1부터 100까지 승급합니다. 각 스킬 에셋의 표에서 랭크 구간, 효과 종류, 첫 랭크 수치, 이후 랭크당 증가분을 지정합니다. 행은 도달한 랭크마다 누적되며, 연습 랭크에 효과를 주려면 시작과 마지막에 0을 입력합니다. 획득 배율과 작업 스태미나, 보너스 확률, 최대 스태미나, 맨손 피해에 연결됩니다. 갈무리는 동물 처치 직후 적용됩니다.

계약서를 받으면 바로 수락합니다. 완료하거나 만료된 농장 계약서는 다음 번 계약을 받을 때 갱신됩니다. 계약서에 우클릭하면 열기와 이동을 선택할 수 있습니다. 문서는 판매할 수 없으며 환생하면 소지품과 함께 초기화됩니다. 아르바이트의 경력별 보수와 선택 보상 아이템은 `Data/Jobs/PotatoDelivery.asset`에서 지정합니다.

기본 시간은 실제 1초에 게임 3분이며 대화 중에는 멈춥니다. 자정이나 스태미나 고갈 시 숙소에서 다음 날을 맞습니다. 무료 잠자리는 스태미나 70, 10G 객실은 100으로 06:00에 시작합니다. 하루가 지나면 채집 기회가 돌아옵니다.

환생 후 스킬 랭크, AP, 누적 레벨, 아르바이트 경력, 농장 부지와 세계 기여가 이어집니다. 이전 저장의 공유 채집 랭크는 수확·벌목·달걀 채집의 초기 랭크로 옮깁니다. 갈무리와 맨손전투는 연습부터 시작합니다. 두 번째 삶부터는 첫 일자리 퀘스트 보상과 레벨 조건으로 다음 환생을 열 수 있습니다. 농장 확장 기부를 마치면 다음 생에 추가 밭이 열립니다. 구름 위 배치와 카메라 구도는 Scene의 `Cloud terrace`, 후보지와 채집 대상은 `Village/First life and gathering`에서 수정합니다.

## 저장과 검증

`Application.persistentDataPath/workerborn-village-v1.json`에 15초마다, 주요 행동 후, 앱 일시정지·종료 시 저장합니다. 저장에는 아이템 배치·수량·장비, 위치, 시간, 성장, 의뢰, 퀘스트, 세계 변화, 작물의 고유 ID별 남은 횟수와 바닥 물품이 포함됩니다. 이전 지갑 방식의 골드는 금화 아이템으로 변환합니다. `.bak` 백업 복구도 지원합니다. 이전 버전 저장 기록은 기존 농장이 완공된 상태로 이어집니다. 첫 생애를 시험하려면 `Village session/Load Saved Game`을 끄세요. 이 상태의 플레이는 기존 저장을 덮어쓰지 않습니다.

```bash
dotnet run --project Tests/HarvestLogic/HarvestLogic.csproj -- --noheader --nocolor --workers=0
python -m pip install -r Tests/requirements.txt
python Tests/validate_harvest_assets.py
python Tests/validate_prefab_contracts.py
python Tests/validate_camera_graphs.py
python Tests/validate_surface_picking.py
```

.NET NUnit 테스트는 두 생애 진행·계약서·납품 원자성·수확·시점 줌·이름 박스 배치와 생성 순서·윤곽의 가까운 점·아이템 배치·수량 거래·장비·저장 변환·시간·마감·보상·성장·환생 규칙을 검증합니다. Unity EditMode에는 셰이더 임포트 오류·메시 클릭 판정의 원본 일치·재질을 교체하지 않는 페이드와 속성 복구·컷오프 속성 부재·감자 텍스처 연결·외형 파츠 조합·본 연결·프리팹의 사전 배치와 UI 구성 검증이 있으며, PlayMode에는 이동·대화·수확·납품·환생·저장 재로드·프리팹 창 연결·이름 클릭 즉시 줍기·수확 유지·고갈 작물의 Collider·이름 박스 상태 색상·카메라 가림 검증이 있습니다. 테스트 저장 폴더는 플레이어 데이터와 분리됩니다.

이 작업 환경에는 Unity Editor가 없어 실제 Unity 6 임포트·PlayMode 실행·미리보기 및 화면 검증은 수행하지 못했습니다. .NET 테스트, C# 구문·참조 컴파일, Scene 및 에셋 연결 정적 검사를 수행했습니다.

원본 메시·텍스처와 `SampleScene`은 보존합니다. 재사용하는 FlatKit 지형 재질의 색상 설정과 대장간 FBX의 콜라이더 임포트 옵션은 마을에 맞게 조정했습니다. `HarvestPrototype`은 기존 수확 전용 데모입니다. `Tools/author_*`는 예제 에셋을 작성할 때 사용한 명시적 오프라인 도구입니다. Unity의 import·Play에서는 실행되지 않습니다. 재실행은 예제 배치를 덮어쓰므로 일반 작업에는 Scene과 Prefab Mode를 사용하세요.

Unity가 실패한 import에서 기본 Renderer 참조를 비워둔 경우, Editor가 준비된 뒤 `VillageRenderPipelineReferences`가 Workerborn PC·Mobile 파이프라인의 **누락된 기본 슬롯만** 복구합니다. 이미 지정한 Renderer와 Renderer Feature는 유지합니다. 수동으로 실행하려면 **Workerborn → Rendering → Repair Missing Renderer References**를 사용합니다.
