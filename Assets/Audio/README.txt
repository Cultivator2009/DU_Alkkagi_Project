효과음 파일 규칙 (SoundLibrary)

이 폴더의 소리 파일은 이름으로 SoundBank에 들어갑니다. 파일을 넣거나 바꾸거나 지우면
Unity가 알아서 다시 읽습니다(수동: Tools > Alkkagi > Load sounds).

이름 = 키 [_세기] [_번호] . wav/ogg/mp3/aiff   (소문자, 숫자, _ 만)
  hit_go.wav, hit_go_2.wav        세기 구분 없는 테이크 (아무 세기에나)
  hit_go_soft_1.wav ...           세기별 테이크: soft(톡) / mid / hard(풀파워)
                                  세기별 파일이 하나라도 있으면 그 키는 세기별만 씁니다.
                                  없는 세기는 가까운 세기를 빌립니다.
  번호가 여럿이면 매번 무작위로(직전 것은 피해서) 고릅니다.

판 위 소리는 알 종류별로 둘 수 있습니다: 키_go / 키_janggi / 키_chess / 키_gonggi.
알 종류별 파일이 없으면 종류 없는 키(예: place.wav)를 씁니다.

판 위 (알 종류별 가능)
  hit       알끼리 부딪힘 (세기별 권장)
  wall      배리어 벽에 부딪힘
  land      서 있던 말이 쓰러지거나 공기알이 판에 내려앉음 (체스, 공기알)
  shatter   알이 깨짐 (판 밖으로 떨어질 때, 체력전 A에서 체력이 다 될 때)
  hinge     장기판 경첩에 부딪힘
  flick     튕기기
  fall      판 밖으로 떨어짐
  place     배치할 때 알 놓기

판 위 (종류 구분 없음)
  crumble   자기장으로 가장자리가 무너짐

인터페이스
  click, tick(턴 초읽기), turn, kill, win, lose, draw, start(대전 시작 박),
  notch(조준 세기 눈금), cancel, stamp(결과 도장), open(창 열기),
  damage(체력전 피해, 세기별 권장), zone_warn(자기장 예고), countdown(랭크전 다음 판 3·2·1)

음악 (Music 폴더, MusicPlayer)
  music_menu      메인 메뉴와 로비 (로비로 가도 이어서)
  music_match     대전 중. music_match_1, _2 ... 여럿이면 판마다 하나 (직전 것은 피해서)
  모두 반복 재생되고, 장면이 바뀌면 1.5초에 걸쳐 바뀝니다. 결과가 뜨면 대전 음악은 사라집니다.
  Music 폴더에 처음 넣는 파일은 스트리밍·Vorbis로 가져옵니다(메모리에 풀어 두지 않음).
  끝과 처음이 이어지게(루프) 만든 파일을 쓰세요. 없으면 조용합니다.

녹음으로 바꿀 때
  - 지금 파일은 코드로 합성한 임시 소리입니다(Tools > Alkkagi > Build sounds).
    녹음을 넣은 뒤에는 Build sounds를 다시 돌리지 마세요. 같은 이름 파일을 덮어씁니다.
  - 합성한 같은 키의 파일은 지우거나 같은 이름으로 덮어쓰세요(남겨 두면 테이크로 섞임).
  - 앞뒤 무음을 자르세요. Unity가 가져올 때 최대 음량을 맞춥니다(Normalize, 기본 켜짐).
