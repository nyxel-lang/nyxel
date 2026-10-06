# Nyxel 语言参考（草稿）

描述语言**是什么**，随设计讨论逐节补全；**为什么**这样定见各节标注的 ADR。还没定的部分标“待定”，不要按猜测实现。

## 总体风格（ADR-0005）

- 代码块用花括号，结构由括号决定，与缩进无关。
- 声明以关键字开头，类型写在名字后面：`hp: int`。
- 语句以换行结束，不写分号。哪些换行不结束语句见“换行与续行”。
- `if` / `while` 的条件不加括号，后面的块必须有花括号。
- 花括号只跟在关键字或声明后面。语句位置不能单独写 `{ ... }`；要早点结束作用域就拆成函数（ADR-0020）。
- **冒号后面是类型，等号后面是值**（ADR-0011）：`var hp: int = 100`、`count: int = 1`、`class Slime : Behaviour`、`Spawn(count = 3)`。例外只有插值字符串里的格式说明符 `{speed:F2}`。

## 换行与续行（ADR-0013）

```nyxel
// 圆括号、方括号里随便换行
let slime = self.World.Spawn<Slime>(
    position = self.Position + offset,
    rotation = Quaternion.Identity)

// 下一行以 . 或 ?. 开头：接着上一行
let targets = self.enemies
    .Where(func(e) = not e.IsDead)
    .OrderBy(func(e) = e.Hp)

// 下一行以二元运算符开头：接着上一行
let canAttack = self.cooldown <= 0
    and target != null
    and not target.IsDead

// 行尾是赋值运算符（= += -= …）或 ->：接着下一行
let nearest =
    self.NearestTo(point = self.Position) ?? return
self.score +=
    enemy.Reward * self.comboMultiplier

if self.IsDead {
    return
} else {                // else 跟在 } 后面同一行
    self.Move()
}
```

- 圆括号、方括号里的换行不结束语句。
- 下一行以 `.`、`?.` 或二元运算符（`+`、`and`、`or`、`??`、`is`、`==` 等）开头时接着上一行。
- 一行以赋值运算符（`=`、`+=`、`-=` 等）或 `->` 结尾时接着下一行（ADR-0013、ADR-0020）。
- 其余换行都结束语句。
- 运算符一律放在行首，括号里也一样（ADR-0019）：二元运算符和 `.`、`?.` 写在行尾都是编译错误，诊断提示挪到下一行开头。
  ```nyxel
  let d = Vector3.Distance(
      self.Position
          + offset,            // 正确
      target)
  let e = Vector3.Distance(
      self.Position +          // 错误
          offset,
      target)
  ```
- `{` 必须和前面的内容在同一行；`else` 必须跟在 `}` 后面同一行。
- 每条语句、每个成员、每个 `case`、每个访问器各占一行；`{` 后面的第一个和 `}` 前面的最后一个可以和括号写在同一行（`get { return self.hp }`、`{ get }`）。

## 文件结构（ADR-0012）

```nyxel
namespace Game.Enemies

import System
import System.Collections.Generic
import Engine

public class Spawner : Behaviour {
    ...
}
```

- 顺序固定：`namespace`（最多一个，作用于整个文件；不写时在全局命名空间）、`import`、声明。
- 类型都写在文件顶层，一个文件可以有多个类型；不能在类型里声明类型（ADR-0020）。.NET 里已有的嵌套类型照常用：`Environment.SpecialFolder.Desktop`。
- `import 命名空间` 导入整个命名空间。每个文件显式列出，没有全局导入和隐式导入。内置类型名不用导入；外层命名空间的类型按 C# 规则可见。
- 待定：导入别名、静态导入。

## 关键字风格（ADR-0006）

关键字用完整英文单词；只有在主流语言里通用、能读出来的缩写可以例外（`var`、`func`、`enum`、`struct`、`init`）。不用 `fn`、`pub`、`mut`、`impl` 这类缩写。拿不准的细节优先参照 Swift。

## 注释（ADR-0007）

```nyxel
/// 在距离最近的出生点生成敌人。
///
/// 文档注释的内容是 Markdown，编译器转成 .NET 的 XML 文档文件。
public func SpawnNearest(self, kind: EnemyKind) -> Entity {
    // 普通行注释
    ...
}
```

- `//` 行注释，`///` 文档注释（Markdown，写在声明前）。没有 `/* */` 块注释。
- 待定：文档注释里参数和返回值的写法约定（倾向 Swift 的 `- Parameter name:` / `- Returns:`）。

## 内置类型与字面量（ADR-0007）

内置类型名沿用 C# 的别名，含义相同：`bool` `byte` `sbyte` `short` `ushort` `int` `uint` `long` `ulong` `float` `double` `decimal` `char` `string` `object`。

数值字面量的类型由上下文（这个位置期望的类型）决定，没有类型后缀：

```nyxel
var speed: float = 2.5                  // float：声明的类型
let half = width * 0.5                  // width 是 float → 0.5 是 float
self.Position = new Vector3(0, 1.5, 0)  // 参数是 float → 1.5 是 float
let t: float = 3                        // 整数字面量可以用在浮点位置
let ratio = 2.5                         // 没有上下文 → double（同 C#）
let count = 10                          // 没有上下文 → int（同 C#）
```

- 没有上下文时：整数是 `int`（放不下依次用 `uint`、`long`、`ulong`），小数是 `double`。
- 混合运算（ADR-0013）：期望类型往里传，但不改变整数运算。

  ```nyxel
  // Wave: int，Speed: float
  slime.Speed = 2 + self.Wave * 0.5   // float
  slime.Speed = self.Wave / 2         // 整数除法再转 float（Wave 是 3 时得 1.0）
  let a = self.Wave * 0.5             // double（没有期望类型）
  let b: int = self.Wave * 1.5        // 错误：期望整数，1.5 不是整数
  let x: float = 1 / 2                // 0.5（两边都是字面量，看期望类型）
  ```

  1. 运算的一边是有确定类型的值（变量、字段、属性、调用结果）时，字面量跟着它。`self.Wave / 2` 不管放在哪里都是整数除法。
  2. 整数值配小数字面量时按浮点算：期望的类型是浮点类型就用它，没有期望类型时用 `double`。
  3. 两边都是字面量时看期望的类型。
  4. 期望的类型来自赋值目标、声明的类型、参数类型、返回类型、比较运算的另一边，经过算术运算符向里传。
- 显式转换写 `x as T`，见“类型转换”。
- 小数字面量不能用在整数位置；超出目标类型范围是编译错误。
- `2.5f`、`10L` 之类的后缀是编译错误。
- `0xFF`、`0b1010`、`1_000_000`、`1e-3` 同 C#。
- 小数点前面的 `0` 不能省：写 `0.5`，`.5` 是编译错误（ADR-0020）。
- 字符字面量 `'a'` 同 C#（ADR-0020），比如 `line.Split(',')`、`text[0] == '#'`。
  - 单引号里是一个 `char`，也就是一个 UTF-16 码元。
  - 转义同字符串：`'\n'`、`'\''`。
  - 双引号永远是 `string`。

## 字符串（ADR-0007）

```nyxel
Log.Info($"HP: {self.hp}/{self.maxHp}")
Log.Info($"Speed: {speed:F2} m/s")
let plain = "{ not interpolated }"
```

- 插值字符串 `$"..."` 与 C# 完全相同：格式说明符、对齐、`{{` / `}}`。
- 普通字符串的转义序列与 C# 相同。

### 原始字符串（ADR-0020）

不处理转义、可以跨行的字符串，与 C# 11 的原始字符串相同。

```nyxel
let pattern = """\d+\.\d+"""            // 单行：\ 是普通字符
let path = """C:\Games\save.json"""

let help = """
    用法：
      spawn <kind> [count]
    """
// 值是 "用法：\n  spawn <kind> [count]"：结尾 """ 前的 4 个空格从每一行去掉

let json = $$"""
    { "hp": {{self.hp}}, "name": "{{self.Name}}" }
    """
```

- 单行写 `"""text"""`。多行写法：`"""` 后面换行，接着写内容各行，结尾的 `"""` 单独占一行。里面没有转义。内容里要出现 `"""` 时，开头和结尾都用更多引号（`""""...""""`）。
- 多行时，结尾 `"""` 前面的空白（缩进）从每一行开头去掉：
  - 某一行不以这段空白开头是编译错误。只含空白的行可以更短，但必须是这段空白的开头部分。
  - 开头 `"""` 那一行的剩余部分、结尾 `"""` 前的换行，都不算内容。
  - 至少要有一行内容；空字符串写 `""`。
- `$"""` 可以插值，写 `{x}`；这时文字里不能有 `{`、`}`。要在文字里写大括号就用 `$$"""`：插值写 `{{x}}`，单个 `{`、`}` 是普通字符。`$` 有几个，插值就用几个大括号。格式说明符和对齐同 `$"..."`。
- 没有逐字字符串 `@"..."`，用原始字符串代替。
- 待定：插值的 `{ }` 里换行（C# 11 允许，目前是编译错误，`$"..."` 也一样）。

## 数组（ADR-0020）

```nyxel
class Shooter : Behaviour {
    var hits: array<RaycastHit> = new array<RaycastHit>(16)

    func Scan(self) {
        let count = Physics.RaycastNonAlloc(self.ray, self.hits)
        for i in 0..<count {
            self.Hit(self.hits[i])
        }
        let parts = self.line.Split(',')    // array<string>
    }
}
```

- 数组类型写 `array<T>`，就是 .NET 的 `T[]`。`array` 是小写关键字，和 `int`、`string` 一样不能当名字用。
- `array<Item?>` 是元素可能为空，`array<Item>?` 是数组本身可能为空；数组的数组写 `array<array<int>>`。
- 指定长度创建：`new array<T>(16)`，也可以写 `new array<T>(length = 16)`。下标、`.Length`、`for ... in` 同 C#。
- 新数组的元素一开始是全零，所以只有全零就是合法值的元素类型能这样创建（ADR-0023）：可空类型（`array<Enemy?>`）、数字、`bool`、`char`、有值为 0 的 case 的简单 enum 和 flags enum（ADR-0024）、第一个 case 不带数据的 `struct enum`（ADR-0024）、C# 定义的 struct（`Vector3`、`RaycastHit`）和 enum。
  - 其余的（class、接口、带数据的 enum、Nyxel 的 struct、没有值为 0 的 case 的 enum）用第二个参数 `element` 给出每个元素，编译成一个循环：
    ```nyxel
    let slots = new array<Slot>(16, func(i) = new Slot(index = i))
    let ps = new array<Particle>(length = 100, element = func(i) = new Particle())
    let grid = new array2d<Cell>(w, h, func(x, y) = new Cell(x = x, y = y))
    ```
  - Nyxel 的 struct 也算在内：全零的元素没有经过它的 `init` 和字段初始值。
  - 让引擎往里填的数组（NonAlloc 系列）写成 `array<Collider?>`，取出来时 `hits[i] ?? continue`。
- 带元素创建用集合字面量：`let primes: array<int> = [2, 3, 5, 7]`，见下一节。
- C# 的 `T[]`、`new T[n]` 是编译错误，诊断给出 `array<T>` 的写法。
- 多维数组（ADR-0022）写 `array2d<T>`、`array3d<T>`，就是 .NET 的 `T[,]`、`T[,,]`：
  ```nyxel
  let grid = new array2d<Tile>(width, height)   // 按位置写各维的长度
  grid[x, y] = Tile.Wall
  for x in 0..<grid.GetLength(0) { ... }
  ```
  - 最多三维。`array2d`、`array3d` 也是关键字。`GetLength`、`Length` 等成员同 .NET。
  - C# 的 `T[,]`、`new T[w, h]` 是编译错误，诊断给出 `array2d<T>` 的写法。
  - 多个下标 `a[i, j]` 也用于 C# 有多个参数的索引器（`Matrix4x4[row, column]`）。
- 待定：带元素创建多维数组（C# 的 `new int[,] { { 1, 2 }, { 3, 4 } }`）。

## 集合字面量（ADR-0022）

```nyxel
let primes: array<int> = [2, 3, 5, 7]
var enemies: List<Enemy> = []                      // 空 List
self.Patrol(points = [self.a, self.b, self.c])     // 参数是 List<Vector3>：创建 List
let speeds: List<float> = [1, 2.5]                 // 元素按 float 处理
let waves: List<int> = [
    3,
    5,
    8,                                             // 末尾可以多一个逗号
]
for dir in [Dir.Up, Dir.Down] {                    // 马上被遍历：不用写类型
    self.Probe(dir)
}
```

- 同 C# 12 的集合表达式。集合的类型来自期望的类型（声明的类型、参数类型、返回类型）：数组、List、Span、`IEnumerable<T>`、`IReadOnlyList<T>` 等 C# 12 支持的类型都可以。元素按元素类型处理，同数字字面量的规则。
- 没有期望的类型是编译错误：`let xs = [1, 2, 3]` 写成 `let xs: List<int> = [1, 2, 3]`。
  - 例外：`for x in [a, b]` 不用写类型，元素类型取元素的共同类型，放在栈上，不分配。
  - `for (dx, dy) in [(0, 1), (1, 0)]` 的元组马上被解构，也不用写名字。
- 没有展开（C# 12 的 `[..a, ..b]`）：先创建，再 `AddRange`。
- 没有字典字面量：先创建，再逐个 `prices["sword"] = 100`。
- C# 的初始化器 `new List<int> { 1, 2 }`、`new Enemy { Hp = 10 }` 是编译错误。
- 待定：展开；字典字面量（等 C# 的字典表达式发布）；对象初始化器（和 `init` 访问器一起讨论，见“属性”）。

## 元组（ADR-0021）

```nyxel
func MinMax(values: List<int>) -> (Min: int, Max: int) {
    ...
    return (lo, hi)                         // 名字来自返回类型
}

let (min, max) = Stats.MinMax(values)       // 解构：按位置拆成两个局部变量
let r = Stats.MinMax(values)
Log.Info($"{r.Min}..{r.Max}")
let limits = (Min = 0, Max = 10)            // 没有期望的类型：带名字
(a, b) = (b, a)                             // 交换
let (quotient, remainder) = Math.DivRem(17, 5)
```

- 元组就是 .NET 的 `ValueTuple`：值类型，不分配。公开 API、要长期存放的、元素超过三个的，建议定义 struct。
- 类型写 `(Min: int, Max: int)`：每个元素都要有名字，用 PascalCase（相当于公开字段，同 .NET 的 `(Quotient, Remainder)`）。`(int, int)` 和 C# 的 `(int Min, int Max)` 是编译错误。至少两个元素。
- 值写 `(lo, hi)`（按位置）或 `(Min = lo, Max = hi)`（带名字），同函数实参；C# 的 `(Min: lo, Max: hi)` 是编译错误。
- 按位置写的值，名字来自期望的类型（返回类型、参数类型、声明的类型）；没有期望的类型时要带名字，`let r = (lo, hi)` 是编译错误。直接被解构的值不需要名字，包括 `for (dx, dy) in [(0, 1), (1, 0)]` 里的元组。
- 元素用名字访问：`r.Min`。`Item1`、`Item2` 只用于 C# 那边没起名字的元组。`==`、`!=` 同 C#。
- 解构按位置，同 C#：
  - `let (min, max) = ...`、`var (a, b) = ...`；`_` 丢弃不要的元素：`let (_, max) = ...`。
  - `(a, b) = (b, a)` 给已有的变量赋值。
  - 元组和有 `Deconstruct` 方法的类型（如字典的 `KeyValuePair`）都能解构。`for` 里也能解构，见“循环与区间”。
  - 变量名和元素名是同一组名字、顺序却不同时给警告：`Stats()` 返回 `(Total: int, Count: int)` 时，`let (count, total) = self.Stats()` 多半是写反了。
  - 解构里只写名字。
- 待定：`match` 里的元组模式（`case (0, 0)`）；嵌套解构；解构时写类型；重写 .NET 里元组元素没有名字的成员。

## 变量与字段（ADR-0006）

```nyxel
class Slime : Behaviour {
    let maxHp: int = 100        // 不可变字段：只能在声明处或 init 里赋值
    var hp: int = 100           // 可变字段

    func Heal(self, amount: int) {
        let healed = amount * 2         // 局部变量，类型推断为 int
        self.hp = Math.Min(self.hp + healed, self.maxHp)
    }
}
```

- `let` 不可变，`var` 可变。不可变的是名字本身，不是它指向的对象：`let enemies = new List<Enemy>()` 之后可以 `enemies.Add(e)`，但不能 `enemies = ...`。
- class 变量存的是引用（对象在哪），struct 变量存的是数据本身（赋值时复制一份）。所以 `let` 的 class 变量仍然可以改对象的字段，`let` 的 struct 整个不可变：不能给它的字段赋值，也不能调用它的 `var self` 方法（ADR-0008）。
- 给 `let` 重新赋值是编译错误。
- 字段必须写类型。局部变量有初始值时可以省略类型。
- 静态字段写 `static var` / `static let`（ADR-0008）。

## 函数与方法（ADR-0006、ADR-0008）

```nyxel
class Slime : Behaviour {
    static var AliveCount: int = 0
    var hp: int = 30

    public func TakeDamage(self, amount: int) {     // 第一个参数是 self：实例方法
        self.hp -= amount
        if self.hp <= 0 {
            self.Die()
        }
    }

    func Die(self) {
        Slime.AliveCount -= 1
        self.Destroy()
    }

    public func Distance(a: Vector3, b: Vector3) -> float {   // 没有 self：静态函数
        return (a - b).Length()
    }
}

struct Health {
    var current: int

    public func IsDead(self) -> bool {          // struct 的 self 方法不能修改自己
        return self.current <= 0
    }

    public func Damage(var self, amount: int) { // var self：可以修改自己
        self.current -= amount
    }
}

slime.TakeDamage(5)                 // 调用时不传 self
let d = Slime.Distance(a, b)        // 静态函数通过类型名调用
```

- `func Name(参数) -> 返回类型 { ... }`；没有返回值时省略 `->` 和返回类型。参数和返回类型必须写。
- 第一个参数是 `self`（不写类型）的是实例方法，没有 `self` 的是静态函数。没有 `static func`。
- `var self` 只能用在 struct 上，表示方法会修改这个值；只写 `self` 的 struct 方法不能修改字段，也不能调用 `var self` 方法。class 方法的 `self` 是引用，总能修改对象的字段，不需要也不允许写 `var self`。
- `var self` 方法不能在 `let` 的 struct 上调用，也不能在临时副本上调用（属性、索引器、函数调用的结果）：
  ```nyxel
  let h = new Health(max = 30)
  h.Damage(amount = 5)                    // 错误：h 是 let
  self.healths[0].Damage(amount = 5)      // 错误：改的是副本，修改会丢失
  var first = self.healths[0]             // 先取出，改完再写回
  first.Damage(amount = 5)
  self.healths[0] = first
  ```
- 待定：C# 里没有标 `readonly` 的 struct 方法在 `let` 和临时副本上怎么处理。
- 参数不能重新赋值，和 `let` 一样（ADR-0014），需要改就复制到局部变量：`let actual = amount - self.armor`。lambda 的参数同样。没有 `var` 参数。
- 实例方法通过实例调用（`slime.TakeDamage(5)`），静态函数通过类型名调用（`Slime.Distance(a, b)`），不能互换。
- 返回值必须写 `return`（ADR-0007）。
- 命名实参写 `名字 = 值`，写不写由调用者决定，混用规则同 C#（ADR-0011）：`self.Spawn(kind = EnemyKind.Slime, count = 3)`。
- 默认参数 `count: int = 1`，规则同 C#。
- 自己声明的函数不能重载（同一类型里不能同名）；`override` / 实现 .NET 里本来就重载的方法除外。
- 函数体可以是块，也可以是 `= 表达式`（ADR-0012）：`public func IsDead(self) -> bool = self.hp <= 0`。赋值不能作为表达式体。

### out / ref 参数（ADR-0015）

```nyxel
if self.cache.TryGetValue(id, out let enemy) {
    enemy.Alert()                       // 这里 enemy 一定有值
}

if not int.TryParse(text, out let count) {
    return
}
self.Spawn(count = count)               // if 后面也能用 count

self.Position = Smooth.Damp(self.Position, target, ref self.velocity, 0.3)
```

- 调用带 `out` / `ref` 参数的方法时，调用处必须写标记：
  - `out let x` / `out var x` 就地声明新变量，类型取参数的类型（有歧义时写 `out let n: int`）。
  - `out 目标` 写进已有的 `var` 局部变量或 `var` 字段；`out _` 丢弃。
  - `ref 目标`：目标必须是 `var` 局部变量、`var` 字段或数组元素，不能是属性或 `let`。
  - 命名实参写 `velocity = ref self.velocity`。C# 的 `in` 参数照常传值，不写标记。
- `if` 条件里用 `out let` 声明的变量，在 `if` 之后仍然可用；其他位置声明的，作用域到所在语句结束。
- 按 .NET 的可空标注收窄：`TryGetValue` 返回 `true` 的分支里，`out` 出来的值非空。
- 自己的函数只在 `override` 或实现接口、.NET 签名要求时才能声明：`data: out SaveData`、`velocity: ref Vector3`、`value: in Matrix4x4`。`out` 参数每条返回路径都要赋值；`ref` / `out` 参数可以赋值（普通参数不行）。自己的 API 表达“可能没有”用 `T?`。

## 扩展（ADR-0014）

```nyxel
namespace Game.Util

import System.Numerics

extension Vector3 {
    /// 去掉高度分量。
    public func Flat(self) -> Vector3 = new Vector3(self.X, 0, self.Z)

    public property IsZero: bool {
        get { return self == Vector3.Zero }
    }

    public func FromAngle(radians: float) -> Vector3 {     // 没有 self：静态扩展
        return new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians))
    }
}

// 另一个文件 import Game.Util 之后：
let ground = self.Position.Flat()
let dir = Vector3.FromAngle(radians = 0.5)
```

- `extension 类型 { }` 写在文件顶层，给不是自己定义的类型（引擎、.NET）加方法和属性。块里的写法和类里一样：有 `self` 是实例方法，没有是静态函数；`static property` 是静态扩展属性。不能有字段和 `init`。
- 可见性同类成员：默认私有（只在这个块里可见），`public` 才能在块外用。使用方 `import` 扩展所在的命名空间后可见。类型本身的成员优先于扩展。
- 扩展只能访问类型的公开成员。
- C# 那边：C# 14 的 `extension` 块，放在 `static partial class Vector3Extensions` 里。
- 待定：泛型扩展（`extension<T> List<T>`）；修改 struct 自己的扩展（`var self`）。

## 函数类型与 lambda（ADR-0012）

```nyxel
var onDeath: func(Enemy) = func(e) { self.score += 10 }    // 没有返回值：不写 ->
let filter: func(Enemy) -> bool = func(e) = e.IsBoss

let alive = enemies.Where(func(e) = e.Hp > 0)

self.button.OnClick(func(evt) {
    self.clicks += 1
})

let nearest = enemies.MinBy(func(e: Enemy) -> float {
    return (e.Position - self.Position).Length()           // 从这个 lambda 返回
})
```

- 函数类型 `func(A, B) -> R`，没有返回值写 `func(A, B)`。对应 .NET 的 `Func<>` / `Action<>`；写 `Func<>` / `Action<>` 给警告。
- lambda 是去掉名字的函数：`func(参数) { 块 }` 或 `func(参数) = 表达式`。参数类型、返回类型能推断时可省略。
- lambda 块里的 `return` 从 lambda 返回；`if` / `match` 块里的 `return` 从外层函数返回。
- 捕获规则同 C#；方法里的 lambda 可以用 `self`。
- 成员函数仍用 `func Name(...)` 声明。
- 在需要函数类型的地方，`对象.方法` / `类型名.函数` 不带括号就是这个函数本身（ADR-0017）：`slime.Died += self.OnSlimeDied`、`names.Select(Slime.Describe)`。别处不带括号写方法名是编译错误。

## 泛型（ADR-0012）

```nyxel
func Max<T: IComparable<T>>(a: T, b: T) -> T {
    return if a.CompareTo(b) >= 0 { a } else { b }
}

class Pool<T: class and IPoolable> {
    var free: Stack<T> = new Stack<T>()
}

let boss = World.Spawn<Slime>()
let biggest = Max(a = 3, b = 7)        // T 推断为 int
```

- `<T>` 同 C#；类型实参推断同 C#。
- 约束写在 `<>` 里，冒号后是类型；多个约束用 `and`。特殊约束 `class`、`struct`、`unmanaged`、`new()`。
- 待定：泛型的可空性（包括 `new array<T>(n)` 能不能直接创建，ADR-0023）；协变 / 逆变。

## 成员访问（ADR-0008）

- 实例成员一律写 `self.`：`self.hp`、`self.Die()`。
- 静态成员一律写类型名：`Slime.AliveCount`、`Slime.Distance(a, b)`。
- 在重写的方法里调用基类的实现写 `super.`：`super.Update(dt = dt)`（ADR-0013）。
- 不带接收者的名字只能是局部变量或参数。直接写字段名是编译错误。
- 私有字段不加前缀。

## 类型定义（ADR-0008）

### class、struct、interface

与 C# 相同：class 是引用类型，struct 是值类型。

```nyxel
interface IDamageable {
    func TakeDamage(self, amount: int)
}

public class Slime : Behaviour, IDamageable {
    ...
    public override func Update(self, dt: float) { ... }
}
```

- 继承和实现接口写在 `:` 后面。`virtual` / `override` / `abstract` / `sealed` 的用词和含义同 C#。
- 接口名以 `I` 开头。接口成员不写可见性，默认公开（同 C#）；实现它的成员要写 `public`。
- 类型默认只在本程序集可见，给其他程序集用写 `public`。

### enum

```nyxel
enum Element {
    case Fire
    case Ice
    case Lightning
}

enum Damage {
    case Physical(Amount: int)
    case Burn(Amount: int, Seconds: float)
    case Heal(Amount: int)
}

let element = Element.Fire                              // 不带数据的 case：常量
let hit = new Damage.Burn(Amount = 12, Seconds = 3)     // 带数据的 case：new
```

- 每种情况一行，以 `case` 开头。
- 全部 case 不带数据时等同 C# 的枚举；有带数据的 case 时，每个值是堆上的对象（`struct enum` 除外，见下）。
- case 的数据同元组（ADR-0024）：
  - 每一项都有名字，用 PascalCase。C# 那边是同名的属性（`hit.Amount`）。
  - 创建时按位置写（`new Damage.Burn(12, 3)`）或带名字写，同元组的值。
  - 用 `match` 按位置取出，同元组的解构（见“match”）。数据创建以后不能改。
- 判断是不是某个 case 写 `x is Damage.Burn`，得到 bool，不取数据（ADR-0024，见“类型检查与收窄”）。
- 带数据的 enum 用 `==` 比内容（ADR-0024）：同一个 case、数据逐个相等就相等，能当字典的键。不带数据的 case 只有一个值，`state == SlimeState.Idle` 就是判断是不是 Idle。
- 待定：带数据的 enum 实现接口；在 enum 里写方法（现在用扩展）。

### enum 的值与 flags（ADR-0024）

```nyxel
enum CookedType : uint {                // 底层类型
    case Mesh = 1                       // 要写值就每个 case 都写
    case Texture = 2
    case Material = 3
}

flags enum Layer {                      // 一个值可以同时包含几个 case
    case None = 0
    case Ground = 1
    case Water = 2
    case Air = 4
    case Solid = Layer.Ground | Layer.Water
}

let mask = Layer.Ground | Layer.Air
if mask.HasFlag(Layer.Air) { ... }
```

- 只有不带数据的 enum 能写值、底层类型和 `flags`。
- 值：
  - 要么每个 case 都写，要么都不写。都不写时按声明顺序 0、1、2，同 C#。
  - 值是整数常量，可以用运算组合（`1 << 3`、`Layer.Ground | Layer.Water`）；引用别的 case 也写类型名。
  - 存档、网络消息、和 C++ 引擎对齐的 enum 建议写明每个值：插入新 case 不会改掉旧的值。
- 底层类型写在冒号后面，同 C#：`byte`、`sbyte`、`short`、`ushort`、`int`、`uint`、`long`、`ulong` 之一，不写是 `int`。
- `flags enum`：
  - 每个 case 都写值，通常每个占一个二进制位，用 `|` 组合。检查包含用 .NET 的 `HasFlag`。
  - Nyxel 的 enum 只有 flags enum 能用 `|`、`&`、`^`、`~`；C# 定义的枚举照 C#，都能。
  - `match` 一个 flags enum（包括 C# 带 `[Flags]` 的）必须写 `else`：组合出来的值列不全。
  - C# 那边是 `[Flags] enum`。`flags` 只在 `enum` 前面是关键字，别处照样能当名字。
- 没有值为 0 的 case 时，数组不能直接创建，要用 `element`（见“数组”）。flags enum 的 0 是“一个都没有”，总能直接创建。

### struct enum（ADR-0024）

```nyxel
struct enum AiState {
    case Idle
    case Chasing(Target: Entity)
    case Fleeing(From: Entity, Until: float)
}

struct Brain {                          // ECS 组件：引擎要求 unmanaged
    var state: AiState = AiState.Idle
}
```

- 带数据的 enum 的值类型版本，同 class 和 struct 的关系：不在堆上分配，赋值时复制。写法和用法（创建、`match`、`is`、`==`）同 `enum`。
- 一个值里留着所有 case 的数据的位置（同类型的可以共用），大小约等于各 case 加起来。case 不能直接包含同一个 struct enum。
- 数据全是 unmanaged 类型时，整个 enum 也是，能放进要求 `T : unmanaged` 的 ECS 组件。
- 全零的值是第一个 case，前提是它不带数据（上例是 `Idle`）；否则数组要用 `element` 创建，同 Nyxel 的 struct。
- 不带数据的 enum 本来就是值类型，写 `struct enum` 是编译错误。

### 构造

```nyxel
struct Point {
    public let X: float
    public let Y: float

    public init(x: float, y: float) {                   // 不带名字的 init：最多一个
        self.X = x
        self.Y = y
    }

    public init FromPolar(radius: float, angle: float) { // 命名 init
        self.X = radius * MathF.Cos(angle)
        self.Y = radius * MathF.Sin(angle)
    }
}

let a = new Point(x = 1, y = 2)
let b = new Point.FromPolar(radius = 1, angle = 0.5)
```

- 不带名字的 `init` 最多一个，其余必须有名字。构造函数不按参数类型重载。
- 没有默认值的字段，每个 `init` 结束前都必须赋过值（确定初始化检查）。
- `init` 不写 `public` 就是私有的。`init` 里的 `self` 是正在构造的对象。
- C# 那边：不带名字的 `init` 是构造函数，命名 `init` 是同名静态方法（`Point.FromPolar(1, 0.5)`）。
- 没写 `init` 的类自动有一个公开的无参 `init`（ADR-0013），前提是每个字段都有初始值，否则编译错误。声明了任何 `init` 就不再自动生成；声明了 `init` 的 struct 不能写 `new Health()`。

### 基类与 super（ADR-0013）

```nyxel
public class Fireball : Projectile {
    public let Radius: float

    public init(power: int, radius: float) {
        super.init(power = power)           // 构造基类部分：必须是第一句
        self.Radius = radius
    }

    public init Small() {
        self.init(power = 5, radius = 0.5)  // 委托给本类的另一个 init
    }

    public override func Update(self, dt: float) {
        super.Update(dt = dt)               // 调用基类的同名方法
    }
}
```

- `super.init(...)` 只能是 `init` 的第一句，参数里不能用 `self`。没写时自动调用基类的无参 `init`，基类没有就是编译错误。
- 本类 `init` 之间委托写 `self.init(...)`，也只能是第一句。
- 重写的方法里调用基类方法写 `super.方法名(...)`。
- C# 那边：`super.init(...)` 是 `: base(...)`，`self.init(...)` 是 `: this(...)`，`super.Update` 是 `base.Update`。
- 待定：调用基类的命名 `init`。

### new

- 创建值一律写 `new`：`new Type(...)`、`new Type.Name(...)`（命名 init 或带数据的 case）、`new List<Enemy>()`。
- 不带 `new` 的 `名字(...)` 一定是函数调用。

## if 表达式（ADR-0007）

```nyxel
let label = if self.hp > 0 { "alive" } else { "dead" }

let speed = if self.IsFrozen {
    0
} else if self.IsHasted {
    self.BaseSpeed * 2
} else {
    self.BaseSpeed
}
```

- `if` 和 `match` 可以作为表达式：分支块里最后一个表达式是该分支的值；当表达式用时必须有 `else`。
- 没有 `?:` 三元运算符。
- 函数体必须用 `return` 返回值。

## match（ADR-0009、ADR-0024）

```nyxel
let change = match hit {
    case Physical(amount) -> -amount
    case Burn(amount, _) -> -amount
    case Heal(amount) -> amount
}

match hit {
    case Burn(_, seconds) -> {      // 按位置取出，不要的写 _
        self.StartBurning(seconds)
    }
    case Physical -> {}             // 不需要数据时不写括号
    else -> {}
}
```

- 每个分支一行，以 `case` 开头；默认分支 `else`。分支用 `->`（Nyxel 没有 `=>`）。
- 分支右边是一个表达式或一个块；当表达式用时，块的值是最后一个表达式。
- case 的数据按位置取出，同元组的解构（ADR-0024）：
  - 名字个数和数据项一样，`_` 丢弃不要的；名字自己起，成为分支里的局部变量。
  - 名字和数据项名是同一组（不分大小写）、顺序却不同时给警告：多半是写反了。
- 必须覆盖所有情况（当语句用也一样）：enum 列全所有 case 或写 `else`；flags enum 和其他类型必须有 `else`。“其余不处理”写 `else -> {}`。
- 模式（ADR-0010）：
  - 常量 `case 0`、`case "boss"`、`case null`；
  - 比较 `case < 10`；区间 `case 10..<50`、`case 1...3`；
  - 组合 `case 1 or 2`、`case > 0 and < 10`、`case not null`；
  - 类型 `case is Enemy`（被匹配的局部变量 / 参数在分支里收窄成该类型）；
  - 附加条件 `case Burn(seconds) when seconds > 3`。

## 空值（ADR-0009）

```nyxel
class Turret : Behaviour {
    var target: Enemy? = null           // 可能为 null
    let owner: Player                   // 一定有值

    func Fire(self) {
        let target = self.FindTarget() ?? return    // 没有就返回
        target.TakeDamage(10)                       // 这里 target 是 Enemy

        let weapon = self.weapon                    // 字段先存进局部变量
        if weapon != null {
            weapon.Reload()                         // 检查过，当作非空
        }

        let name = self.target?.Name ?? "nobody"
        let player = World.FindPlayer()
            ?? throw new InvalidOperationException("场景里没有玩家")
    }
}
```

- `T` 一定有值，`T?` 可能为 `null`，违反是编译错误。值类型 `int?` 同 C#。
- C# 程序集的可空标注照单全收；没有标注的程序集，返回值和字段当作 `T?`，参数可以接受 `null`。
- 检查后自动收窄：只对局部变量和参数；字段、属性、调用结果先存进局部变量。
- `?.`、`??` 同 C#；`??` 右边可以是 `return`、`throw`、`continue`、`break`。
- 和 `null` 比较只看引用是不是空的（ADR-0023）：`x == null` 是 C# 的 `x is null`，不调用类型重载的 `==`。所以 `== null`、`!= null`、`?.`、`??`、`case null`、收窄的意思都一样。两个值之间的 `a == b` 照常调用重载。“已销毁”这类状态用引擎提供的属性或方法查（`world.IsAlive(entity)`），不借用 null。
- `x ??= value`（ADR-0023）：`x` 是 null 时才赋值，同 C#，是语句。局部变量和参数之后当作非空；字段不收窄。
  ```nyxel
  name ??= "无名"                          // 之后 name 当作非空
  self.Target ??= self.FindNearestPlayer()  // 字段：只赋值
  ```
- 没有 `x!` / `x!!`；断言非空写 `?? throw new 异常("原因")`。
- 数组元素一开始是全零，所以 `new array<Enemy>(16)` 是编译错误，见“数组”。

## 循环与区间（ADR-0010、ADR-0022）

```nyxel
for enemy in self.enemies {
    enemy.Alert()
}

for i in 0..<self.waveCount {       // 0 到 waveCount - 1
    self.SpawnWave(i)
}

for level in 1...3 {                // 1、2、3
    self.Unlock(level)
}

for i in (0..<self.enemies.Count).Reversed() {  // 倒着走：边删边遍历不会跳过
    if self.enemies[i].IsDead {
        self.enemies.RemoveAt(i)
    }
}

for x in (0..<self.width).StepBy(2) {           // 0、2、4、...
    self.PlacePillar(x)
}

for (i, item) in self.inventory.Index() {   // 同时拿下标（.NET 的 Index()，import System.Linq）
    self.slots[i].Show(item)
}

for (name, score) in self.scores {  // 字典：每一项拆成键和值
    Log.Info($"{name}: {score}")
}

while self.hp > 0 {
    ...
}
```

- `for 名字 in 集合`，循环变量不可变。`while 条件 { }`。没有 C 风格的 `for`，没有 `do` / `while`。
- `for (a, b) in 集合` 把每个元素按位置解构（ADR-0021，见“元组”）。
  - 字典写 `for (key, value) in dict`。
  - 同时拿下标用 .NET 的 `Index()`：`for (i, item) in list.Index()`。编译器把它翻译成计数器加普通遍历，不分配，结果和 `Index()` 相同。
  - 只要下标、或者要边遍历边改元素时，写 `for i in 0..<list.Count`。
- `a..<b` 不含 `b`，`a...b` 含 `b`。没有单独的 `..`。
- 区间的优先级比算术低、比比较高（ADR-0019）：`0..<self.count - 1` 是 `0..<(count - 1)`。比较、`and` / `or`、`??` 写在两边要加括号。区间不能连写（`a..<b..<c` 是错误）。
- 倒序和步长（ADR-0022）：`for` 里的区间后面可以接 `.Reversed()`、`.StepBy(n)`，翻译成普通的 `for`，不分配。
  - 边界和正着遍历一样，不用自己算 `Count - 1`。
  - 步长必须是正数。两个可以连用，按书写顺序生效：`(0..<10).StepBy(4).Reversed()` 是 8、4、0。
- 区间不是值（ADR-0022）：只能写在 `for ... in` 后面、`case` 后面和切片里。`let r = 0..<10`、把区间传给函数都是编译错误。判断在不在区间里用 `match` 的 `case 1...3` 或比较运算。
- `break` / `continue` 同 C#。没有循环标签（ADR-0021）：要从里层循环直接跳出外层，就把循环拆成函数，用 `return` 跳出。
- 待定：区间作为值（以后有需要再讨论）。

### 切片（ADR-0022）

```nyxel
let prefix = name[0..<3]                // 前 3 个字符
let middle = items[2...4]               // 第 2、3、4 个
let rest = name[1...]                   // 从第 1 个到末尾
let head = items[..<3]                  // 从开头到第 3 个之前
let last = items[items.Count - 1]       // 最后一个
let stem = path[..<path.Length - 4]     // 去掉末尾 4 个字符
```

- 同 C# 8 的范围下标，写法用 Nyxel 的区间：`name[0..<3]` 是 C# 的 `name[0..3]`，`items[2...4]` 是 `items[2..5]`。
- 只有切片能省略一边：省略开头写 `..<b` 或 `...b`；省略结尾只写 `a...`（C# 的 `a..`）。
- 能切的类型和结果同 C#：字符串得到新字符串，数组和 List 复制出新的一份（会分配，改它不影响原来的），`Span` 只指向原来的那一段。
- 没有 C# 的 `^1`（从末尾数）：最后一个写 `items[items.Count - 1]`，数组和字符串用 `Length`。`^` 只是异或。

## 运算符（ADR-0010）

```nyxel
if not self.IsDead and target != null {
    self.Attack(target)
}
let canJump = self.onGround or self.coyoteTime > 0
self.combo += 1
```

- 逻辑运算用单词 `and`、`or`、`not`（短路、优先级同 C#）。没有 `&&`、`||`、`!`。
- 没有 `++` / `--`，写 `+= 1`。
- 算术、比较、位运算、复合赋值、整数除法、隐式数值转换同 C#。`??=` 见“空值”；Nyxel 的 enum 只有 flags enum 能位运算，见“enum 的值与 flags”。
- 赋值（包括 `+=` 等）是语句，不产生值：不能 `a = b = 0`，不能写在条件里（ADR-0011）。
- 优先级从高到低（ADR-0019）：成员访问、调用、下标 → 一元（`-` `+` `~` `not` `await` `launch`）→ `as` → `*` `/` `%` → `+` `-` → `<<` `>>` → `..<` `...` → `<` `>` `<=` `>=` `is` → `==` `!=` → `&` → `^` → `|` → `and` → `or` → `??`。除了 `as`（ADR-0014）和区间，其余同 C#。`??` 从右往左结合，区间不结合，其余从左往右。

## 类型检查与收窄（ADR-0010、ADR-0024）

```nyxel
func OnHit(self, other: Collider) {
    if other is Pickup {
        other.Collect()             // 这里 other 是 Pickup
    }
    if other is not Enemy {
        return
    }
    other.TakeDamage(5)             // 这里 other 是 Enemy
}
```

- `x is T` 得到 bool；`x is not T` 是否定。`is` 后面只能写类型或 enum case。
- 被检查的是局部变量或参数时自动收窄（同空值收窄的规则），不另起变量名。
- `x is 类型.Case` 判断是不是某个 enum case（ADR-0024）：`if self.state is SlimeState.Idle`、`hit is not Damage.Heal`。只判断，不取数据，也不收窄；要数据写 `match`。

## 类型转换（ADR-0014）

```nyxel
let cells = self.distance as int                 // 向零截断：2.7 → 2，-2.7 → -2
let percent = self.current as float / self.Max   // 先转换再除，不是整数除法
let index = MathF.Round(self.t * 10) as int      // 要四舍五入就先 Round
let code = Element.Fire as int                   // 枚举和整数互转

let p = other as Projectile                      // 错误：可能失败，用 if other is Projectile
```

- `as` 是转换，不是类型检查，不会因为“类型不对”而失败。允许：数字之间、枚举和整数之间、子类转父类或接口、C# 类型定义的显式转换运算符。
- 可能失败的向下转换写 `as` 是编译错误，用 `is` 收窄。
- 语义同 C# 的强制转换：向零截断，不检查溢出，常量超出范围是编译错误。整数转成 enum 也不检查：`5 as Element` 可能不是任何 case，列全了 case 的 `match` 遇到它时抛异常，同 C# 的 switch 表达式（ADR-0024）。
- 优先级比 `*` `/` 高，比一元运算符和成员访问低。
- 没有 C# 的 `(int)x` 写法。

## 错误处理（ADR-0014）

```nyxel
try {
    let text = File.ReadAllText(path)
    self.Apply(text)
} catch e: FileNotFoundException {
    Log.Warn($"没有存档：{path}")
} catch e: IOException when e.HResult == 32 {
    Log.Warn("文件被占用")
} finally {
    self.loading = false
}

let config = try { Config.Load(path) } catch e: IOException { Config.Default }
```

- 用 .NET 的异常。`catch 名字: 类型`，名字必须写；捕获所有异常写 `catch e: Exception`。`when` 过滤同 C#。
- 被前面的 `catch` 完全覆盖、永远到不了的 `catch` 是编译错误。`try` 后面至少有一个 `catch` 或 `finally`。
- `} catch`、`} finally` 跟在 `}` 后面同一行。
- `try` 可以当表达式，规则同 `if` / `match`：每个分支最后一个表达式是值，或者以 `return` / `throw` 离开；`finally` 不产生值。
- `throw 异常对象` 是语句。在 `catch e` 里 `throw e` 重新抛出同一个异常，保留原来的调用栈（翻译成 C# 的 `throw;`）。
- `finally` 里不能 `return`。
- 待定：文档注释里写会抛哪些异常的约定。

## 资源释放（ADR-0015）

```nyxel
func ReadSave(self, path: string) -> string {
    using stream = File.OpenRead(path)
    using reader = new StreamReader(stream)
    return reader.ReadToEnd()
}   // 离开这个块时释放：先 reader 后 stream
```

- `using 名字 = 值`：值必须实现 `IDisposable`，在所在块结束时调用它的 `Dispose()`。多个按声明的相反顺序释放；`return`、`break`、异常离开时也一样。值为 `null` 时跳过。
- `using` 自己引入名字，不写 `let`（同 `for x in`、`catch e: T`）。名字不能重新赋值。需要时可以写类型：`using stream: FileStream = ...`。
- `using` 只有这一种用法：导入是 `import`，没有块形式的 `using (...) { }`，没有 `defer`。要更早释放就拆成函数；和 `IDisposable` 无关的清理用 `try` / `finally`。
- 待定：`await using`（`IAsyncDisposable`）。

## 协程（ADR-0016）

```nyxel
import Nyxel                            // Wait 在 Nyxel.Runtime 里

async func Intro(self) {
    self.Camera.Focus(self)
    await Wait.Seconds(2)               // 暂停 2 秒，游戏照常运行
    self.Roar()
    let text = await File.ReadAllTextAsync(self.path)
}

async func Load(self, name: string) -> Level { ... }

func OnHit(self) {
    launch self.FlashRed()              // 启动，不等
}

let job = launch self.Load(name = "Forest")
let level = await job                   // 之后再等结果
```

- `async func` 声明会跨帧的函数，可以有返回值（`-> R`）。lambda 写 `async func(e) { }`，函数类型写 `async func(A) -> R`。重写时 `async` 必须和基类一致。不能有 `out` / `ref` 参数。
- `await` 只能写在 `async` 函数里，能等 Nyxel 的协程、.NET 的 `Task` / `ValueTask`、任何有 `GetAwaiter` 的对象。恢复后的代码在主线程、下一次调度时执行。
- 结果可以等待的调用（`async` 函数、返回 `Task` 的 .NET 方法）只能直接写在 `await` 或 `launch` 后面，否则编译错误。
- `launch 调用` 得到协程的句柄：`job.Stop()` 停止，`await job` 等它完成。
- `launch obj.方法()` 的协程绑定在 `obj` 上：`obj` 被引擎销毁后，下一次恢复时自动停止。静态函数和没有生命周期的对象启动的协程不绑定。
- 停止时 `finally` 和 `using` 的释放照常执行，`catch` 接不住。`await` 一个被停止的协程，等待方也停止。
- `launch` 出去的协程里没被接住的异常交给宿主记日志。
- 热重载前所有协程都会停止。
- 等待函数（Nyxel.Runtime，`Nyxel` 命名空间）：`Wait.Seconds(s)`、`Wait.NextFrame()`、`Wait.Until(func() = 条件)`。
- 待定：同时等多个协程；句柄类型和 `Wait` 系列的最终命名。

## 属性（ADR-0011）

```nyxel
class Player : Behaviour {
    var hp: int = 100                       // 字段

    public property IsDead: bool {          // 计算属性
        get { return self.hp <= 0 }
    }

    public property Hp: int {
        get { return self.hp }
        set(value) { self.hp = Math.Clamp(value, 0, 100) }
    }

    public property Score: int = 0 {        // 自动属性：编译器存值
        get
        private set                         // 外部只读
    }
}

interface IDamageable {
    property Health: int { get }
}
```

- `property` 声明属性；`var` / `let` 只声明字段。
- 访问器要么都带函数体（计算属性），要么都不带（自动属性，初始值写在类型后面）。每个访问器一行，可以单独加可见性。
- setter 的新值在括号里命名：`set(value)`。访问器里 `self` 隐式可用。静态属性写 `static property`。
- 默认用字段；实现接口的属性、对外只读对内可写、值需要计算时用属性。
- 待定：只能在构造时设置的访问器（C# 的 `init`），以及 C# 设置它们用的对象初始化器 `new T { X = 1 }`；索引器；`field` 关键字。

## 事件（ADR-0017）

```nyxel
public class Slime : Behaviour {
    public event Died(slime: Slime)
    public event HealthChanged(before: int, after: int)

    func Die(self) {
        emit self.Died(slime = self)        // 没人订阅时什么也不发生
        self.Destroy()
    }
}

// 在 Spawner 里订阅
slime.Died += self.OnSlimeDied              // 绑定 self：Spawner 销毁后自动移除
slime.HealthChanged += func(before, after) {
    self.hud.Shake()
}
slime.Died -= self.OnSlimeDied              // 提前取消
```

- `event 名字(参数)` 声明事件，参数写法同函数参数，没有返回值和默认值。可以加 `public`、`protected`、`static`，接口里也能声明。
- `emit 接收者.事件(实参)` 触发，只有声明事件的类型能 `emit`（子类也不行）。处理函数按订阅顺序同步执行，异常传给 `emit` 的一方。
- `+=` / `-=` 订阅和取消，C# 和引擎的事件也一样。
- 订阅绑定在“执行谁的代码”上：`对象.方法` 绑定那个对象，lambda 绑定写它的 `self`。绑定对象被引擎销毁后，订阅在下一次触发时自动移除；热重载时脚本做过的所有订阅都会移除。对 lambda 写 `-=` 是编译错误。
- C# 那边是普通的 C# `event`。
- 待定：事件访问器（`add` / `remove`）；一个处理函数抛异常时是否继续通知其他处理函数。

## 状态机（ADR-0017）

没有专门语法：状态用 enum 写，每种状态需要的数据放在 case 里，在 `Update` 里 `match` 当前状态；按顺序推进的流程用协程。

```nyxel
enum SlimeState {
    case Idle
    case Chasing(Target: Player)
    case Fleeing(From: Player)
}

match self.state {
    case Idle -> self.LookAround()
    case Chasing(target) -> self.MoveTowards(target = target.Position, dt = dt)
    case Fleeing(from) -> self.MoveAway(from = from.Position, dt = dt)
}
```

- 追击时才有 `target`，不会出现“处于 Idle 却拿着一个过期的 target”。
- 新加一个状态，所有没处理它的 `match` 都会编译报错。
- 状态要放进 ECS 组件（只能是 unmanaged）、或者每帧都在切换时，用 `struct enum`（ADR-0024），不在堆上分配。

## 可见性（ADR-0006、ADR-0008）

- 成员默认私有；公开写 `public`，只给子类用写 `protected`（ADR-0015），含义与 C# 相同。struct 的成员不能是 `protected`。
- 类型默认只在本程序集可见；给其他程序集用写 `public`。
- 重写时可见性必须和基类一致：基类是 `protected virtual` 就写 `protected override`。
- 没有 `internal`，也没有 C# 的 `protected internal`、`private protected`。
- 修饰符的顺序固定（ADR-0019）：可见性（`public` / `protected` / `private`）→ `static` → `abstract` / `sealed` / `virtual` → `override` → `async`。顺序不对、重复都是编译错误，诊断给出正确的写法：`public static var`、`protected override func`、`public override async func`、`sealed override`。

## 命名（ADR-0006）

编译器检查，不符合给警告。

| 什么 | 写法 | 例 |
|---|---|---|
| 类型、方法、属性、`public` / `protected` 字段、命名 init、enum case 和它的数据、元组元素 | PascalCase | `EnemySpawner`、`TakeDamage`、`SpawnInterval`、`FromPolar`、`Burn`、`Seconds`、`Min` |
| 参数、局部变量、非公开字段 | camelCase，不加前缀 | `amount`、`spawnPoint`、`hp` |

调用 .NET API 时名字原样使用。待定：缩略词的大小写（`Id` / `IO`）。

## 待定

大的主题都讨论过了。剩下的细节记在各节末尾的“待定”里，写 samples 或实现时遇到再逐个讨论。还没定的写法一律是编译错误：以后放开不会让已有代码失效。诊断见 design/diagnostics.md。

不属于任何一节的：特性（C# 的 `[Serializable]`、`[Obsolete]`）还没讨论过，roadmap 记在 M3。
