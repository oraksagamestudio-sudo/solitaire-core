# Foundation Reverse Moves Patch (FreeCell core)

이 패치는 FreeCell 코어에 **파운데이션 → 테이블로/셀** 역이동을 추가합니다.
CLI는 이미 `f2t` / `f2c` 단축어를 지원하므로, 코어만 따라오면 바로 동작합니다.

> 대상 프로젝트: `src/Solitaire.FreeCell`

## 적용 개요
1) `MoveKind` enum에 새 항목 추가:
   - `FoundationToTableau`
   - `FoundationToCell`

2) `FreeCellState.GetLegalMoves()`에 역이동을 **나열**하는 코드 추가

3) `FreeCellState.Apply(Move)`에 역이동을 **적용**하는 분기 추가

아래 단계는 **C# 7.3 (netstandard2.0)** 호환으로 작성되었습니다.

---

## 0) 브랜치 준비
```bash
cd ~/dev/solitaire-core
git checkout -b feat/foundation-reverse
```

## 1) MoveKind enum 수정
파일: `src/Solitaire.FreeCell/FreeCellTypes.cs` (또는 Move/MoveKind가 선언된 파일)

**찾기 (search):**
```
public enum MoveKind
{
    TableauToCell,
    CellToTableau,
    TableauToFoundation,
    CellToFoundation,
    TableauToTableau
```

**수정 (아래 2줄 추가):**
```
public enum MoveKind
{
    TableauToCell,
    CellToTableau,
    TableauToFoundation,
    CellToFoundation,
    TableauToTableau,
    FoundationToTableau,   // <-- add
    FoundationToCell       // <-- add
```

저장 후 진행.

---

## 2) FreeCellState.GetLegalMoves() 보강
파일: `src/Solitaire.FreeCell/FreeCellState.cs`

**권장 위치:** 기존에 `TableauToFoundation` / `CellToFoundation` 등을 나열하는 구간 **아래**에 붙여넣기.

**추가 코드:**
```csharp
// === Reverse from Foundation ===
// 규칙:
//  - Foundation → Tableau : 빈 테이블로는 어떤 카드도 가능(FreeCell 규칙),
//    비어있지 않으면 목적지 top 카드와 '색 다름' + '랭크 = 목적지-1' 이어야 함.
//  - Foundation → Cell : 빈 셀이 하나라도 있으면 가능 (count=1)
for (int f = 0; f < 4; f++)
{
    int topRank = this.FoundationTop[f];
    if (topRank <= 0) continue; // empty foundation pile
    var fromCard = new Card((Suit)f, (Rank)topRank);

    // to tableau
    for (int t = 0; t < this.Tableaus.Length; t++)
    {
        var pile = this.Tableaus[t];
        bool ok;
        if (pile.Count == 0)
        {
            ok = true; // FreeCell: any card to empty tableau
        }
        else
        {
            var dest = pile[pile.Count - 1];
            // alternating colors & descending by 1
            ok = IsOppositeColor(fromCard, dest) && ((int)dest.Rank == (int)fromCard.Rank + 1);
        }
        if (ok)
            yield return new Move(MoveKind.FoundationToTableau, f, t, 1);
    }

    // to cell (first empty cell)
    for (int c = 0; c < this.Cells.Length; c++)
    {
        if (!this.Cells[c].HasValue)
        {
            yield return new Move(MoveKind.FoundationToCell, f, c, 1);
            break;
        }
    }
}
```

> **참고:** `IsOppositeColor(Card a, Card b)` 헬퍼가 없다면 아래와 같이 간단히 추가할 수 있습니다.
> (파일 상단 유틸 함수 모음에 배치)
> ```csharp
> static bool IsRed(Suit s) { return s == Suit.Heart || s == Suit.Diamond; }
> static bool IsOppositeColor(Card a, Card b) { return IsRed(a.Suit) != IsRed(b.Suit); }
> ```

---

## 3) FreeCellState.Apply(Move) 분기 추가
파일: `src/Solitaire.FreeCell/FreeCellState.cs`

**찾기 (search):**
```
switch (m.Kind)
{
    case MoveKind.TableauToCell:
        ...
        break;
    // ...
    case MoveKind.TableauToTableau:
        ...
        break;
    default:
        throw new NotSupportedException(...)
}
```

**수정 (아래 2개 분기 추가):**
```csharp
case MoveKind.FoundationToTableau:
{
    // from: foundation index (0=♠,1=♥,2=♦,3=♣)
    // to: tableau index
    if (m.Count != 1) throw new InvalidOperationException("Foundation->Tableau supports count=1 only.");
    if (m.From < 0 || m.From >= 4) throw new ArgumentOutOfRangeException("From");
    if (m.To < 0 || m.To >= this.Tableaus.Length) throw new ArgumentOutOfRangeException("To");

    int top = this.FoundationTop[m.From];
    if (top <= 0) throw new InvalidOperationException("Source foundation empty.");

    var card = new Card((Suit)m.From, (Rank)top);
    var dst = new List<Card>(this.Tableaus[m.To]);
    if (dst.Count == 0)
    {
        // ok
    }
    else
    {
        var destTop = dst[dst.Count - 1];
        if (!(IsOppositeColor(card, destTop) && ((int)destTop.Rank == (int)card.Rank + 1)))
            throw new InvalidOperationException("Illegal Foundation->Tableau move.");
    }

    // build new state
    var newTabs = (List<Card>[])this.Tableaus.Clone();
    dst.Add(card);
    newTabs[m.To] = dst;

    var newFound = (int[])this.FoundationTop.Clone();
    newFound[m.From] = top - 1; // pop card

    return new FreeCellState(
        newTabs,
        (Card?[])this.Cells.Clone(),
        newFound,
        this.MoveCount + 1,
        this.Config
    );
}
case MoveKind.FoundationToCell:
{
    if (m.Count != 1) throw new InvalidOperationException("Foundation->Cell supports count=1 only.");
    if (m.From < 0 || m.From >= 4) throw new ArgumentOutOfRangeException("From");
    if (m.To < 0 || m.To >= this.Cells.Length) throw new ArgumentOutOfRangeException("To");

    int top = this.FoundationTop[m.From];
    if (top <= 0) throw new InvalidOperationException("Source foundation empty.");
    if (this.Cells[m.To].HasValue) throw new InvalidOperationException("Target cell not empty.");

    var newCells = (Card?[])this.Cells.Clone();
    newCells[m.To] = new Card((Suit)m.From, (Rank)top);

    var newFound = (int[])this.FoundationTop.Clone();
    newFound[m.From] = top - 1;

    return new FreeCellState(
        (List<Card>[])this.Tableaus.Clone(),
        newCells,
        newFound,
        this.MoveCount + 1,
        this.Config
    );
}
```

> **주의:** 위 생성자는 예시입니다. 실제 `FreeCellState` 생성자/팩토리 시그니처에 맞게
> 전달 인자를 조정하세요. (필요 시 `With(...)` 스타일 클론 생성자 사용)

---

## 4) 빌드 & 확인
```bash
dotnet build ./Solitaire.sln -c Release
dotnet test ./Solitaire.sln -c Release   # (있다면)
dotnet run --project src/Solitaire.Cli -- repl
> fc-new --seed 1
> fc-legal
> fc-move t2f 2 0
> fc-move f2t 0 5
> fc-move f2c 1 2
```

## 5) 커밋 & 푸시
```bash
git add -A
git commit -m "feat(freecell): allow reverse moves from foundation (f2t/f2c)"
git push --set-upstream origin feat/foundation-reverse
```

---

문서/코드 라인 형태가 프로젝트마다 조금씩 다를 수 있어요.
적용 중 컴파일 오류가 나면 오류 메시지와 함께 알려주세요. 해당 위치에 맞춰 **정확한 덮어쓰기용 파일**을 만들어 드릴게요.
