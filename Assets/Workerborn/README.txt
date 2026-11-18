WORKERBORN — 솔바람 마을

Unity 6000.3.5f2 / URP 17.3 / Input System 1.17
Scenes/VillagePrototype.unity를 열어 Play합니다.

Scene에는 완성된 프리팹 인스턴스를 배치합니다. Play 시 마을을 생성하지 않습니다.
Prefabs/UI: 각 창의 실제 내부 구성, HUD, 셀/행/아이콘/버튼/이름 박스/연결선.
Data/VillageUI: UI 창과 동적 위젯 프리팹 연결 목록.
Prefabs/Characters: 주인공과 NPC 전체.
Prefabs/Interaction: 게시판, 우물, 몬스터 예제.
Prefabs/Crops: 수확 가능한 작물 전체.
Prefabs/Items: 물품별 줍기 프리팹. 수량, Collider, 시각 오브젝트 등을 설정.
Prefabs/World: 건물, 가로등, 나무, 소품과 장애물.

공통 WorldInteractable을 NPC, 작물, 아이템, 몬스터가 상속합니다.
거리, 이름 위치, 요청/완료 이벤트를 설정하고 유형별 Interact/OnArrived를 확장합니다.
작물/아이템을 Scene에서 복제하면 서로 다른 저장 ID를 부여합니다.

Data/NPCs: NPC의 역할, 대화, 일자리, 퀘스트, 상점, 외형.
Data/Characters: NPC와 주인공의 공통 외형 형식. 부위별 선택과 회전/확대 3D 미리보기.
Data/Characters/Player_Default: 주인공 기본 외형.
Data/Characters/Parts: 머리, 의상, 눈, 눈썹, 입, 양손 소품.
Data/VillageSettings: 시간, 휴식, 성장, 환생.
Data/Jobs, Quests, Dialogues, Items: 일자리, 퀘스트, 대화, 아이템과 장비.
Data/Potato: 수확 횟수/시간/드롭 수와 줍기 프리팹.
Data/ToonPalette, Materials/Toon: 제공된 FlatKit 셰이더를 사용하는 카툰 재질 매핑.
Rendering: PC/모바일 파이프라인과 FlatKit 오브젝트 외곽선 기능.

우클릭 또는 휠 버튼 드래그: 자유 시점 회전. 휠: 줌.
Ctrl: 가까운 NPC/몬스터 윤곽 강조, 커서에서 가까운 윤곽점까지 연결선.
Ctrl+클릭: 그 대상에게 접근.
바닥 아이템 이름은 상시 표시. Alt: 검정 반투명 이름 박스 표시.
이름 박스를 눌러도 같은 줍기 명령을 실행합니다.
이름은 위아래로 겹침을 풀고 화면 높이가 같으면 생성 순서대로 배치합니다.
큰 더미는 Alt를 누르고 이름 위에서 휠로 세로 스크롤합니다.
I 소지품 / Q 퀘스트 / Z 또는 K 스킬 / C 캐릭터 / M 미니맵.
소지품은 클릭으로 집고 다시 클릭해 놓습니다. 오른쪽 클릭이나 Esc로 복귀합니다.
상점에서 구입 수량을 고르거나, 집은 물건을 판매 영역에 눌러 판매합니다.

전체 편집 방법, 저장, 검증은 루트 README.md를 참조하세요.
.NET 로직 테스트와 정적 연결 검사를 수행했습니다.
Unity Editor가 없는 환경이므로 실제 PlayMode/셰이더/미리보기 화면 검증은 미실행입니다.
원본 에셋팩과 SampleScene은 보존했습니다.
한국어 폰트는 Noto Sans CJK KR 부분 추출본입니다. Fonts/OFL.txt 참조.
