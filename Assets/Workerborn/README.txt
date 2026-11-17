WORKERBORN — 감자 수확 프로토타입

실행
- Unity 6000.3.5f2에서 프로젝트를 엽니다.
- Workerborn > Harvest Prototype > Open Scene 메뉴로 시험 씬을 엽니다.
  또는 Assets/Workerborn/Scenes/HarvestPrototype.unity를 직접 엽니다.
- Play를 누릅니다. 농장, 캐릭터, UI와 이동용 NavMesh는 실행 시 생성됩니다.

조작
- 감자 작물 클릭: 가까이 이동한 뒤 한 번 수확합니다.
- 머리 위 스킬 풍선과 진행 막대가 작업 상태를 표시합니다.
- 수확이 완료되면 남은 수확 횟수가 1 줄고 감자 2개가 바닥으로 떨어집니다.
- 떨어진 감자 클릭: 가까이 이동해 1개를 줍습니다. 수확만으로 가방에 들어가지는 않습니다.
- 땅 클릭: 이동합니다. 진행 중인 작업이 있다면 취소합니다.
- 우클릭 / Esc: 이동 또는 작업을 취소합니다. 미완료 수확은 횟수를 소비하지 않습니다.
- R / '밭 다시 준비' 버튼: 작물, 바닥 아이템, 가방 수량, 캐릭터 위치를 초기화합니다.
- 금색 점은 해당 작물의 남은 수확 횟수입니다. 0회가 되면 식물이 사라지고 흙은 남습니다.

조정
- Data/Potato.asset의 Harvests Per Plant, Harvest Seconds, Items Per Harvest를 조정합니다.
  기본값은 작물당 3회, 회당 2.4초, 감자 2개입니다.
- 다른 CropDefinition을 만들면 작물별 수확 횟수/작업 시간/수확량/외형을 따로 정의할 수 있습니다.
- 씬의 Workerborn Harvest Prototype 오브젝트에서 밭의 행과 열을 바꿀 수 있습니다.
- 기존 Idle, Walk, Attack_sword 애니메이션을 재사용하며 Playables로 작업 중 공격 동작을 반복합니다.
- 원본 에셋과 SampleScene은 수정하지 않습니다. 시험 씬을 빌드 목록의 첫 번째에 추가했습니다.

검증
- Window > General > Test Runner의 EditMode에서 HarvestCycleTests를 실행합니다.
- PlayMode의 HarvestFlowTests는 이동-수확-드롭-줍기, 작업 취소, 고갈/초기화를 검사합니다.
- Tests/HarvestLogic/HarvestLogic.csproj는 .NET 8 SDK에서 동일한 수확 로직 테스트를 실행합니다.
  dotnet run --project Tests/HarvestLogic/HarvestLogic.csproj -- --noheader --nocolor --workers=0

현재 범위
- 이번 시험은 직접 수확하고 떨어진 감자를 줍는 조작감에 집중합니다.
- 가방은 획득 수량만 표시합니다. 격자 인벤토리, 시간표, 에너지, AP, 환생, 저장은 후속 범위입니다.
- 한국어 UI 폰트는 Noto Sans CJK KR의 이름을 바꾼 부분집합이며 Fonts/OFL.txt에 출처와 라이선스가 있습니다.
