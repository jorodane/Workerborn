WORKERBORN — 솔바람 마을 프로토타입

실행
- Unity 6000.3.5f2에서 프로젝트를 엽니다.
- Workerborn > Village Prototype > Play Village 메뉴를 선택합니다.
- 또는 Scenes/VillagePrototype.unity를 열고 Play를 누릅니다.
- 마을, NPC, 농장, UI, 이동 경로는 Play에서 생성됩니다.
- 전체 조작과 첫 플레이 순서는 프로젝트 루트 README.md에 있습니다.

첫 플레이
- 서쪽 농장주 미라와 대화해 아르바이트를 받습니다. [Q]에서도 미라에게 길을 안내할 수 있습니다.
- 감자를 수확하고 바닥에 떨어진 감자를 주워, 18:00 전에 미라에게 납품합니다.
- '마을의 새 일꾼' 퀘스트 보상을 받고 [K]에서 AP로 수확 스킬을 올립니다.
- 노아의 잡화점에서 거래하고 레나의 여관에서 잠을 잡니다.
- 퀘스트 보상을 받고 레벨 3이 되면 북쪽 기억의 샘에서 환생할 수 있습니다.
- 농장에 100G와 감자 20개를 기부하면 다음 환생 후 밭과 의뢰 규모가 커집니다.

조작
- 좌클릭: 이동 / 작물 수확 / 감자 줍기 / NPC와 대화
- 우클릭: 이동·작업 취소. 인벤토리의 빵을 우클릭하면 사용합니다.
- I 소지품, Q 퀘스트, K 스킬, C 캐릭터, M 지도, F1 가까운 작물 수확, F2 빵 사용
- 마우스 휠: 확대·축소, 가운데 버튼 드래그: 시점 회전
- 창 제목 표시줄을 드래그해 이동하고 Esc로 닫을 수 있습니다.
- 대화 중 시간은 멈추고 일반 정보 창을 여는 동안에는 시간이 흐릅니다.

조정과 저장
- Data/Potato.asset: 수확 횟수, 작업 시간, 기본 드롭 수, 식물/아이템 프리팹
- Runtime/Village/VillageState.cs: 시간 배율, 의뢰, 성장, 경제, 환생 규칙
- Runtime/Village/VillagePrototype.cs: 마을 배치와 NPC 연결
- 저장: Application.persistentDataPath/workerborn-village-v1.json (15초 주기 및 주요 행동 후 자동 저장)
- 저장에는 가방 배치, 위치, 시간, 성장, 의뢰, 퀘스트, 남은 수확과 바닥 물품이 포함됩니다.
- HarvestPrototype.unity는 기존 수확만 시험하는 별도 씬입니다. R 초기화는 그 씬에서만 작동합니다.
- 기존 Idle, Walk, Attack_sword 애니메이션을 재사용합니다. 원본 에셋은 수정하지 않습니다.

검증
- Unity Test Runner EditMode: HarvestCycleTests, GridInventoryTests, VillageStateTests
- Unity Test Runner PlayMode: HarvestFlowTests, VillageFlowTests
- PlayMode 테스트는 플레이어 저장과 분리된 임시 폴더를 사용합니다.
- .NET 8: dotnet run --project Tests/HarvestLogic/HarvestLogic.csproj -- --noheader --nocolor --workers=0
- 연결 검사: python Tests/validate_harvest_assets.py
- 한국어 UI 폰트는 Noto Sans CJK KR를 이름 변경·부분 추출했습니다. Fonts/OFL.txt 참조.
