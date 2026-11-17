WORKERBORN — 솔바람 마을

Unity 6000.3.5f2 / URP 17.3 / Input System 1.17
Scenes/VillagePrototype.unity를 열어 Play합니다.
Workerborn > Village Prototype > Play Village 메뉴로도 실행합니다.

마을, 농장, NPC, 주인공, 카메라와 기본 HUD는 Scene에 저장되어 있습니다.
Village/Buildings, Nature, Paths, Farm, NPCs를 Play 전부터 직접 수정합니다.
마을을 Play 때 새로 생성하지 않습니다. 길찾기 데이터만 선택적으로 계산/베이크합니다.

Data/NPCs: NPC 설정. Inspector에서 외형을 펼쳐 파츠별 변경과 3D 미리보기.
Data/Characters/Player_Default: 주인공 기본 외형. NPC와 같은 형식.
Data/Characters/Parts: 머리, 의상, 눈, 눈썹, 입, 손 소품. 위치/회전/크기/재질/색상.
미리보기 드래그: 회전. 휠: 확대. 에셋 변경은 Editor의 Scene 외형에 반영합니다.
Data/VillageSettings: 기본 외형, 시간, 휴식, 경험치와 환생 조건.
Data/Jobs, Quests, Dialogues, Items: 일자리, 퀘스트, 대화, 아이템과 장비.
Data/Potato: 작물별 수확 횟수, 작업 시간, 드롭 수와 프리팹.

우클릭 드래그: 3인칭 자유 시점. 휠: 확대·축소.
좌클릭: 이동, 수확, 바닥 아이템 줍기, NPC와 대화.
I 소지품 / Q 퀘스트 / Z 또는 K 스킬 / C 캐릭터 / M 미니맵.
아이템 좌클릭으로 집고 다음 좌클릭으로 놓습니다. 우클릭·Esc는 집기 취소.
아이템 우클릭: 사용·장착·분할. Ctrl+클릭: 사용·장착.
소지품 왼쪽은 장비 칸입니다. 괭이는 장착 시 작업 효과를 줍니다.
골드도 실제 아이템입니다. HUD에는 시계, 미니맵, 상태와 창 메뉴가 표시됩니다.

미라의 아르바이트를 받아 수확한 감자를 직접 줍고 납품합니다.
첫 일자리 퀘스트와 레벨 3 조건을 갖추면 기억의 우물에서 환생합니다.
농장에 100G와 감자 20개를 기부하면 다음 환생에 확장 그룹이 활성화됩니다.
확장 전 12개, 확장 후 24개의 작물이 Scene에 이미 배치되어 있습니다.

전체 편집 방법, 조작, 저장과 검증은 루트 README.md를 참조하세요.
Unity Editor가 없는 작업 환경이므로 Unity PlayMode·화면 검증은 미실행입니다.
원본 에셋과 SampleScene은 보존했습니다.
한국어 폰트는 Noto Sans CJK KR 부분 추출본입니다. Fonts/OFL.txt 참조.
