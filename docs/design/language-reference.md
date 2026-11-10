# Nyxel 语言参考（草稿）

描述语言**是什么**，随设计讨论逐节补全；**为什么**这样定见各节标注的 ADR。还没定的部分标“待定”，不要按猜测实现。

## 总体风格（ADR-0005）

- 代码块用花括号，结构由括号决定，与缩进无关。
- 声明以关键字开头，类型写在名字后面：`hp: int`。
- 语句以换行结束，不写分号。哪些换行不结束语句见“换行与续行”。
- `if` / `while` 的条件不加括号，后面的块必须有花括号。
- 花括号只跟在关键字或声明后面，`{ }` 块本身没有值（ADR-0025）。语句位置不能单独写 `{ ... }`：在自己的作用域里算一个值用 `eval`（见“if 与 eval”），否则拆成函数（ADR-0020）。
- **冒号后面是类型，等号后面是值**（ADR-0011）：`var hp: int = 100`、`count: int = 1`、`class Slime : Behaviour`、`Spawn(count = 3)`。例外只有插值字符串里的格式说明符 `{speed:F2}`。
- `_` 单独出现表示“这个位置的值不要”（ADR-0026）：解构 `let (_, max) = ...`、`out _`、取 case 数据 `case Burn(_, seconds)`、默认分支 `case _`、`catch _: T`。它是保留记号，不能当名字和值；`_x`、`x_` 仍是普通名字。待定：`for _ in 0..<n`、lambda 参数写 `_`。

## 换行与续行（ADR-0013）

```nyxel
// 圆括号、方括号里随便换行
let slime = self.World.Spawn<Slime>(
    position = self.Position + offset,
    rotation = Quaternion.Identity)

// 下一行以 . 或 ?. 开头：接着上一行
let targets = self.enemies
    .Where(func(e) = not e.IsDead)
    .OrderBy(func(e) = e.HP)

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

// 当值用的 if / try（-> 形式）：下一行以 else、catch、finally 开头时接着上一行
let speed = if self.IsFrozen -> 0
    else -> self.BaseSpeed
```

- 圆括号、方括号里的换行不结束语句。
- 下一行以 `.`、`?.`、`with`（ADR-0027）或二元运算符（`+`、`and`、`or`、`??`、`is`、`==` 等）开头时接着上一行。
- 一行以赋值运算符（`=`、`+=`、`-=` 等）或 `->` 结尾时接着下一行（ADR-0013、ADR-0020）。
- `->` 形式的 `if` / `try`：下一行以 `else`、`catch` 或 `finally` 开头时接着上一行（ADR-0025）。`match` 的默认分支是 `case _`，所以行首的 `else` 一定属于前面的 `if`（ADR-0026）。
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
- `{` 必须和前面的内容在同一行；`{ }` 形式的 `else`、`catch`、`finally` 必须跟在 `}` 后面同一行。
- 每条语句、每个成员、每个 `case`、每个访问器各占一行；`{` 后面的第一个和 `}` 前面的最后一个可以和括号写在同一行（`get { return self.hp }`、`{ get }`）。

## 文件结构（ADR-0012、ADR-0033）

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

### 同名类型和命名空间别名（ADR-0033）

```nyxel
import System                       // System.Random
import Engine                       // Engine.Random
import ep.protocol under Pb         // 协议生成的代码

public enum LogLevel : byte { ... } // 自己的

let rng = new System.Random()       // 两个命名空间都有 Random：写全名
let level = Pb.LogLevel.Warn        // 协议里的 LogLevel；不带前缀的 LogLevel 是自己的
```

- 导入的两个命名空间有同名类型时，只导入不报错，用到这个名字时是编译错误：写全名，或者给其中一个命名空间起别名。
- 自己所在的命名空间（和外层）里的类型优先于导入的，不报错。
- `import 命名空间 under 别名`：这个命名空间里的类型写成 `别名.类型名`。别名只引入这一个名字，不导入命名空间里的类型；要直接写名字，另写 `import 命名空间`。
- 只能给命名空间起别名，不能给类型起。别名只在这个文件里有效，不能和另一个别名、看得见的类型或命名空间同名。`under` 只在导入行是关键字。
- 没有静态导入（C# 的 `using static`）：成员前面总是写类型名，`MathF.Sqrt(x)`。不带接收者的名字只能是局部变量或参数（ADR-0008）。
- C# 的 `using Pb = ep.protocol`、`import ep.protocol as Pb` 是编译错误，诊断给出 `under` 的写法；`using static` 也是编译错误。

## 程序入口（ADR-0036、ADR-0039）

```nyxel
class Program {
    static func Main() {
        Console.WriteLine("Hello, Nyxel")
    }
}
```

- 输出可执行程序时（`nyxel run`、输出类型是可执行文件的项目），程序从任意一个类型里的 `static func Main` 开始执行，同 C#。游戏脚本由引擎调用，不需要入口。
- 写法：`static func Main()`、`static func Main(args: array<string>)`，各自可以加 `-> int` 返回退出码。可见性随意；不能写在泛型类型里。
- 没有 `Main`、有两个以上、签名不对都是编译错误。`async func Main` 暂时是编译错误（协程要靠宿主驱动，M4）。
- 编译成库时不找入口，叫 `Main` 的函数就是普通的函数。

## 关键字风格（ADR-0006）

关键字用完整英文单词；只有在主流语言里通用、能读出来的缩写可以例外（`var`、`func`、`enum`、`struct`、`init`）。不用 `fn`、`pub`、`mut`、`impl` 这类缩写。拿不准的细节优先参照 Swift。

## 注释（ADR-0007、ADR-0035）

```nyxel
/// 在距离最近的出生点生成敌人。
///
/// 文档注释的内容是 Markdown，编译器转成 .NET 的 XML 文档文件。第一段是摘要，补全列表里显示这一句。
///
/// - Parameter kind: 生成哪一种。
/// - Returns: 生成的敌人。出生点由 [`Spawner.NearestPoint`] 找出。
/// - Throws InvalidOperationException: 场景里一个出生点都没有时。
public func SpawnNearest(self, kind: EnemyKind) -> Entity {
    // 普通行注释
    ...
}
```

- `//` 行注释，`///` 文档注释（Markdown，写在声明前）。没有 `/* */` 块注释。
- 文档注释的第一段是摘要（XML 的 `<summary>`），其余段落是 `<remarks>`（ADR-0035）。
- 参数、返回值、会抛的异常写成列表项，放在最后，按这个顺序（ADR-0035）：
  - `- Parameter 名字: 说明`：每个参数一行，按声明的顺序。函数、`init`、事件、索引器的参数，还有泛型参数（`- Parameter T:`），都这样写。
  - `- Returns: 说明`。
  - `- Throws 类型: 说明`：类型的写法同代码。编译器不检查函数是不是真的会抛这个异常（同 C#）。
  - 都不是必须写的，名字和类型已经说清楚时就不写。
  - 不用 Swift 的分组写法（`- Parameters:` 下面缩进列出），也不用 C# 的 `<param>`、Javadoc 的 `@param`：编译器给警告并给出这里的写法。
- 链接到别的代码写成方括号里一个代码片段 `` [`名字`] ``（ADR-0035）：类型直接写名字，成员写 `类型.成员`，参数和泛型参数直接写名字。只有反引号的 `` `x` `` 是普通的代码片段，不是链接。
- 参数名不存在、链接不到、`- Throws` 后面不是异常类型时给警告。

## 内置类型与字面量（ADR-0007、ADR-0013、ADR-0037、ADR-0038）

内置类型名沿用 C# 的别名，含义相同：`bool` `byte` `sbyte` `short` `ushort` `int` `uint` `long` `ulong` `float` `double` `decimal` `char` `string` `object`。

数值字面量没有类型后缀，类型由它旁边的东西决定。总原则：**字面量跟着旁边有类型的值；没有的话看这个位置期望的类型；都没有就用 C# 的默认类型**（整数是 `int`，放不下依次用 `uint`、`long`、`ulong`；小数是 `double`）。

```nyxel
var speed: float = 2.5                  // 期望的类型：float
let half = width * 0.5                  // 跟着 width（float）：float
self.Position = new Vector3(0, 1.5, 0)  // 参数是 float：1.5 是 float
let t: float = 3                        // 整数字面量可以用在浮点位置
let ratio = 2.5                         // 都没有：double（同 C#）
let count = 10                          // 都没有：int（同 C#）
```

- 期望的类型来自赋值目标、声明的类型、参数类型、返回类型、比较运算的另一边，经过算术运算符向里传。调用的结果不看外面期望的类型（见“调用里的字面量”）。
- 小数字面量不能用在整数位置；超出目标类型范围是编译错误。
- `2.5f`、`10L` 之类的后缀是编译错误。
- `0xFF`、`0b1010`、`1_000_000`、`1e-3` 同 C#。
- 小数点前面的 `0` 不能省：写 `0.5`，`.5` 是编译错误（ADR-0020）。
- 显式转换写 `x as T`，见“类型转换”。
- 字符字面量 `'a'` 同 C#（ADR-0020），比如 `line.Split(',')`、`text[0] == '#'`。
  - 单引号里是一个 `char`，也就是一个 UTF-16 码元。
  - 转义同字符串：`'\n'`、`'\''`。
  - 双引号永远是 `string`。

### 运算里的字面量（ADR-0013、ADR-0038、ADR-0042）

期望的类型往里传，但不改变整数运算：

```nyxel
// Wave: int，Speed: float
slime.Speed = 2 + self.Wave * 0.5   // float
slime.Speed = self.Wave / 2         // 错误：整数除法的结果不能悄悄变成 float（见下）
let half = self.Wave / 2            // int：整数除法（Wave 是 3 时得 1）
let a = self.Wave * 0.5             // double（没有期望的类型）
let b: int = self.Wave * 1.5        // 错误：期望整数，1.5 不是整数
let c: float = 1.5 * 2              // 3.0：两边都是字面量，看期望的类型
let d: float = 1 / 2                // 错误：整数相除是整数除法，写 1.0 / 2
let e: int = 7 / 2                  // 3，同 C#
```

1. 运算的一边是有确定类型的值（变量、字段、属性、调用结果）时，字面量跟着它。`self.Wave / 2` 不管放在哪里都是整数除法；它的结果要当浮点用得写明（见“整数相除的结果”）。
2. 整数值配小数字面量时按浮点算：期望的类型是浮点类型就用它，没有期望的类型时用 `double`。
3. 两边都是字面量时看期望的类型。例外：整数字面量之间的 `/` 和 `%` 永远是整数运算，这样的运算出现在按浮点算的字面量表达式里是编译错误，诊断提示把一边写成小数（ADR-0038）。“按浮点算”指期望的类型是 `float`、`double`、`decimal`，或者没有期望的类型但式子里有小数字面量：`let x: float = 10 / 2`、`let y = 1 / 2 * 4.0` 都报错。期望整数类型、或者没有期望的类型且全是整数时照 C#：`let n = 1 / 2` 是 0。

### 调用里的字面量（ADR-0037）

调用 .NET 的重载时，字面量跟着有类型的实参：

```nyxel
// speed: float，wave: int，weights: array<float>
self.speed = Math.Max(self.speed, 0.5)      // Max(float, float)（C# 选 double 的那个）
let at = Array.IndexOf(self.weights, 0.5)   // IndexOf<float>：0.5 是 float
let same = speed.Equals(0.5)                // Equals(float)，不是 Equals(object)
let a = Math.Max(self.wave, 0.5)            // double：没有重载能原样收下 int 又放得下 0.5
let m = Math.Max(1, 2.5)                    // double：只有字面量，用默认类型
```

1. 先找有类型的实参（变量、字段、调用结果等）原样传进去、字面量也放得下的重载；字面量能变成数值类型时，不考虑把它装箱成 `object` 的重载。只有一个最合适的就选它，字面量取它的参数类型。
2. 找不到或者不止一个时，字面量用默认类型（`int` / `double`），按 C# 的规则选。
3. 还是一个都不适用时，小数字面量也可以变成 `float` / `decimal`（`MathF.Sqrt(2.5)`）。
- 泛型推断同样：类型实参由有类型的实参推出，字面量跟着它。
- 调用不看外面期望的类型（同 C#）：`self.speed = Math.Max(self.wave, 0.5)` 是 `double`，要写 `as float`。
- 跟着 `float` 实参的字面量表达式同样不能有整数相除：`Math.Max(self.speed, 1 / 2)` 是编译错误（ADR-0038）。

### 整数相除的结果（ADR-0042）

整数相除会丢掉小数部分。丢了以后再变成浮点，多半不是写的人想要的，所以这一步不能隐式发生：

```nyxel
// kills、total、wave: int
let ratio: float = kills / total                // 错误：3 / 4 已经是 0
let exact: float = kills as float / total       // 0.75：先把一边转成浮点，按浮点除
let cells: float = (kills / total) as float     // 0.0：确实要整除，对结果写 as
let percent: float = kills / total * 100        // 错误：整除之后再乘，还是那个 0
let scaled = wave / 2 * 0.5                     // 错误：wave / 2 的结果要和 0.5 相乘
let smooth = wave / 2.0 * 0.5                   // 字面量写成小数就按浮点除
let half = wave / 2                             // int：没有变成浮点，照常
```

- 两边都是整数的 `/`，连同它外面再做的整数加、减、乘、取余、取负和括号，这样一个整数算式的值不能隐式转换成 `float`、`double`、`decimal`。
- 隐式转换出现的地方都算：赋值和声明的类型、实参、返回值、和浮点值做运算或比较、`+=` 这类复合赋值的右边。
- 两种写法：要小数就先把一边转成浮点（`kills as float / total`，字面量写成小数 `wave / 2.0`）；确实要整除就对结果写 `as`（`(kills / total) as float`）。
- 相除的结果先传给要整数的参数（`Math.Abs(a / b)`）、写了 `as`、当移位的操作数时，表明要的就是整数，之后怎么用都行。单独的 `%` 不受影响。
- 只有字面量的 `1 / 2` 见上面“运算里的字面量”。

## 字符串（ADR-0007）

```nyxel
Log.Info($"HP: {self.hp}/{self.maxHP}")
Log.Info($"Speed: {speed:F2} m/s")
let plain = "{ not interpolated }"
```

- 插值字符串 `$"..."` 与 C# 相同：格式说明符、对齐、`{{` / `}}`。
- `{ }` 在开始的那一行闭合（ADR-0035）：C# 11 起允许洞跨行，Nyxel 里是编译错误。长的表达式先用 `let` 算出来，再写进 `{ }`。
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
- 插值的 `{ }` 同样在开始的那一行闭合（ADR-0035）。

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
  - 其余的（class、接口、`class enum`、Nyxel 的 struct、没有值为 0 的 case 的 enum）用第二个参数 `element` 给出每个元素，编译成一个循环：
    ```nyxel
    let slots = new array<Slot>(16, func(i) = new Slot(index = i))
    let ps = new array<Particle>(length = 100, element = func(i) = new Particle())
    let grid = new array2d<Cell>(w, h, func(x, y) = new Cell(x = x, y = y))
    ```
  - Nyxel 的 struct 也算在内：全零的元素没有经过它的 `init` 和字段初始值。
  - 元素类型是类型参数 T 时也要写 `element`（ADR-0031）：编译器不知道 T 的全零合不合法。元素类型是 `T?`（有 `class` 或 `struct` 约束）的可以直接创建，全是 null。
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
- C# 的集合初始化器 `new List<int> { 1, 2 }` 是编译错误。对象初始化器 `new Enemy { HP = 10 }` 写成 `new Enemy() with (HP = 10)`，见“创建时设置成员”。
- 待定：展开；字典字面量（等 C# 的字典表达式发布）。

## 元组（ADR-0021）

```nyxel
static func MinMax(values: List<int>) -> (Min: int, Max: int) {
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

## 变量与字段（ADR-0006、ADR-0036、ADR-0042）

```nyxel
class Slime : Behaviour {
    let maxHP: int = 100        // 不可变字段：只能在声明处或 init 里赋值
    var hp: int = 100           // 可变字段

    func Heal(self, amount: int) {
        let healed = amount * 2         // 局部变量，类型推断为 int
        self.hp = Math.Min(self.hp + healed, self.maxHP)
    }
}
```

- `let` 不可变，`var` 可变。不可变的是名字本身，不是它指向的对象：`let enemies = new List<Enemy>()` 之后可以 `enemies.Add(e)`，但不能 `enemies = ...`。
- class 变量存的是引用（对象在哪），struct 变量存的是数据本身（赋值时复制一份）。所以 `let` 的 class 变量仍然可以改对象的字段，`let` 的 struct 整个不可变：不能给它的字段赋值，也不能调用它的 `var self` 方法（ADR-0008）。
- 给 `let` 重新赋值是编译错误。
- 字段必须写类型。局部变量的类型可以省略，由初始值推断。
- 局部变量必须在声明的地方给初始值（ADR-0042）；字段可以留给 `init` 赋值。`let label: string` 之后再各分支赋值是编译错误：

  ```nyxel
  let label = if wave > 2 -> "late" else -> "early"     // 分几种情况算一个值：-> 形式的 if / match / try，或 eval
  var best: Enemy? = null                               // 一开始没有：写成可空的
  ```
- 静态字段写 `static var` / `static let`（ADR-0008）。
- `static let` 的初始值是常量表达式（字面量、不带数据的 enum case、别的常量和它们的运算）、类型是数字、`bool`、`char`、`string` 或 enum 时，它是编译期常量（C# 那边是 `const`），可以当默认参数值、`match` 的 `case` 标签和 enum case 的值（ADR-0038）。其余的 `static let`（`Vector3`、数组、调用的结果）是启动时算一次的只读字段（C# 的 `static readonly`），用在这些位置是编译错误，诊断说明它的初始值不是常量表达式。读的一方不用区分：两种都是“不会变的值”。

  ```nyxel
  class Spawner {
      static let DefaultCount: int = 3                      // 编译期常量
      static let Origin: Vector3 = Vector3.Zero             // 只读字段

      func Spawn(self, count: int = Spawner.DefaultCount) { }
  }
  ```
- 一个函数里，一个名字只指一个变量（ADR-0036）：声明局部变量或参数时，不能和这一点看得见的局部变量、参数同名。

  ```nyxel
  let speed = 2.0
  for i in 0..<count {
      let speed = speed * i                   // 错误：外层已经有 speed
  }
  self.enemies.Where(func(speed) = ...)       // 错误：lambda 的参数也一样
  ```

  - 适用于所有引入名字的地方：`let` / `var`、循环变量、解构、`case let`、`catch`、`out let`、`using`、lambda 的参数和 lambda 里的局部变量（比 C# 严：C# 允许 lambda 里同名）。
  - 并列的块、并列的分支可以重用名字。局部变量从声明的地方开始可见，到所在的块结束，所以块结束以后再声明同名的变量也可以。
  - 字段不受影响（总是写 `self.hp`）；`_` 是丢弃，不算名字。

## 函数与方法（ADR-0006、ADR-0008、ADR-0032、ADR-0039）

```nyxel
class Slime : Behaviour {
    public static var AliveCount: int = 0
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

    public static func Distance(a: Vector3, b: Vector3) -> float {   // 没有 self：静态函数，写 static
        return (a - b).Length()
    }
}

struct Health {
    var current: int = 30

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
- 第一个参数是 `self`（不写类型）的是实例方法；没有 `self` 的是静态函数，必须写 `static func`（ADR-0039）。两个标记要一致：`static func F(self)`、不带 `static` 的 `func F()` 都是编译错误，诊断说明加哪个或去掉哪个。`init`、`operator` 不写：`init` 总是实例的，运算符总是静态的。
- `var self` 只能用在 struct 上，表示方法会修改这个值；只写 `self` 的 struct 方法不能修改字段，也不能调用 `var self` 方法。class 方法的 `self` 是引用，总能修改对象的字段，不需要也不允许写 `var self`。
- `var self` 方法不能在 `let` 的 struct 上调用，也不能在临时副本上调用（属性、`List` 这类普通索引器、函数调用的结果）：
  ```nyxel
  let h = new Health()
  h.Damage(amount = 5)                    // 错误：h 是 let
  self.healths[0].Damage(amount = 5)      // 错误（healths 是 List<Health>）：改的是副本，修改会丢失
  var first = self.healths[0]             // 先取出，改完再写回
  first.Damage(amount = 5)
  self.healths[0] = first
  self.slots[0].Damage(amount = 5)        // 能（slots 是 array<Health>）：数组元素就是数组里的格子
  ```
- 哪些写法拿到的是副本，同 C#（ADR-0032）。“改”指给字段赋值、调用 `var self` 方法：

  | 写法 | 拿到的是 | 能改吗 |
  |---|---|---|
  | `var` 局部变量；class 的字段；`var self` 方法里的 `self.x` | 存储本身 | 能 |
  | `let` 局部变量和字段；`self` 方法里的 `self.x`；`for x in` 的 `x` | 存储本身，只读 | 不能 |
  | 数组元素 `arr[i]` | 数组里的格子 | 能，`let arr` 也能 |
  | `Span<T>` 的元素、返回 `ref` 的函数（`world.Get<T>(e)`） | 原来的存储 | 能，`let` 的 `Span` 也能 |
  | `ReadOnlySpan<T>` 的元素、返回 `ref readonly` 的函数 | 原来的存储，只读 | 不能 |
  | 属性、普通索引器（`list[i]`、`dict[key]`）、普通函数的返回值 | 副本 | 不能，改了会丢 |

  ```nyxel
  let transforms = chunk.Get<Transform>()                  // Span<Transform>：直接指向引擎的内存
  transforms[i].Rotation = Quaternion.Normalize(...)       // 写进引擎的内存，不复制
  world.Get<Transform>(e).Position = spawnPoint            // ref 返回值：改的是组件本身
  var t = world.Get<Transform>(e)                          // 这是复制一份：没有 ref 局部变量
  ```
- C# 的 struct 方法：标了 `readonly` 的（和 `readonly struct` 的方法）同 Nyxel 的 `self` 方法，哪里都能调用。没标的可能会改自己，同 C#，在只读的值或副本上调用时是在一份副本上调用；但这时**不用它的返回值是编译错误**（ADR-0032）：只为副作用调用，要么改动会丢，要么只是库作者漏标了 `readonly`。
  ```nyxel
  for chunk in world.Query<Transform>().Read<Spin>() { ... }   // 能：Read 没标 readonly，但返回值用上了
  let ready = cd.Ready()                                         // 能：用了返回值
  for cd in self.cooldowns { cd.Tick(dt) }     // 错误：cd 是只读的，Tick 的改动会丢
  self.weapon.Cooldown.Tick(dt)                // 错误：Cooldown 是属性，改的是副本；先存进 var，改完写回
  ```
- 待定：ref 局部变量（C# 的 `ref var t = ref world.Get<Transform>(e)`）。
- 参数不能重新赋值，和 `let` 一样（ADR-0014），需要改就复制到局部变量：`let actual = amount - self.armor`。lambda 的参数同样。没有 `var` 参数。
- 实例方法通过实例调用（`slime.TakeDamage(5)`），静态函数通过类型名调用（`Slime.Distance(a, b)`），不能互换。
- 返回值必须写 `return`（ADR-0007）。
- 命名实参写 `名字 = 值`，写不写由调用者决定，混用规则同 C#（ADR-0011）：`self.Spawn(kind = EnemyKind.Slime, count = 3)`。
- 默认参数 `count: int = 1`，规则同 C#。
- 自己声明的函数不能重载（同一类型里不能同名）；`override` / 实现 .NET 里本来就重载的方法除外。运算符和转换按操作数类型可以写几个（ADR-0030）。
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

## 扩展（ADR-0014、ADR-0031、ADR-0032）

```nyxel
namespace Game.Util

import System.Numerics

extension Vector3 {
    /// 去掉高度分量。
    public func Flat(self) -> Vector3 = new Vector3(self.X, 0, self.Z)

    public property IsZero: bool {
        get { return self == Vector3.Zero }
    }

    public static func FromAngle(radians: float) -> Vector3 {     // 静态扩展
        return new Vector3(MathF.Cos(radians), 0, MathF.Sin(radians))
    }
}

// 另一个文件 import Game.Util 之后：
let ground = self.Position.Flat()
let dir = Vector3.FromAngle(radians = 0.5)
```

- `extension 类型 { }` 写在文件顶层，给不是自己定义的类型（引擎、.NET）加方法和属性。块里的写法和类里一样：有 `self` 是实例方法，`static func` 是静态函数；`static property` 是静态扩展属性。不能有字段和 `init`，暂时也不能有索引器（ADR-0029：C# 14 的 extension 块还不支持）。可以写运算符，不能写转换（ADR-0030：C# 14 不支持）。
- 可见性同类成员：默认私有（只在这个块里可见），`public` 才能在块外用。使用方 `import` 扩展所在的命名空间后可见。类型本身的成员优先于扩展。
- 扩展只能访问类型的公开成员。
- C# 那边：C# 14 的 `extension` 块，放在 `static partial class Vector3Extensions` 里。
- 泛型扩展（ADR-0031）：类型参数写在 `extension` 后面，后面的类型再用它；约束照旧写在 `<>` 里。
  ```nyxel
  extension<T> List<T> {
      public func Shuffle(self, rng: Random) { ... }
      public func Map<U>(self, convert: func(T) -> U) -> List<U> { ... }   // 只有这个方法用的 U 写在方法上
  }

  extension<T: IComparable<T>> T {                  // 扩展所有可比较的类型：5.Clamp(lo = 0, hi = 3)
      public func Clamp(self, lo: T, hi: T) -> T { ... }
  }

  extension List<int> {                             // 具体类型不需要类型参数
      public func Total(self) -> int { ... }
  }
  ```
  - `extension` 后面的每个类型参数都要在被扩展的类型里出现；只有某些成员用的写在那个成员上。
  - C# 那边：`extension<T>(List<T> self) where T : ...`，放在 `ListExtensions` 里。
- 扩展 struct 时可以写 `var self`，规则同 struct 的方法（ADR-0032）：
  ```nyxel
  extension Transform {
      public func MoveBy(var self, delta: Vector3) {
          self.Position += delta
      }
  }

  world.Get<Transform>(e).MoveBy(delta)     // ref 返回值：改的是引擎里的组件
  transforms[i].MoveBy(delta)               // Span 的元素
  self.ghosts[0].MoveBy(delta)              // 错误（ghosts 是 List）：改的是副本
  ```
  - 被扩展的类型要是 struct（或有 `struct` 约束的 T）；class 的扩展写 `var self` 是编译错误。只写 `self` 的扩展方法不能改自己的字段。
  - C# 那边：`extension(ref Transform self)`。

## 函数类型与 lambda（ADR-0012）

```nyxel
var onDeath: func(Enemy) = func(e) { self.score += 10 }    // 没有返回值：不写 ->
let filter: func(Enemy) -> bool = func(e) = e.IsBoss

let alive = enemies.Where(func(e) = e.HP > 0)

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

## 泛型（ADR-0012、ADR-0031）

```nyxel
static func Max<T: IComparable<T>>(a: T, b: T) -> T {
    return if a.CompareTo(b) >= 0 -> a else -> b
}

class Pool<T: class and IPoolable> {
    var free: Stack<T> = new Stack<T>()
}

let boss = World.Spawn<Slime>()
let biggest = Max(a = 3, b = 7)        // T 推断为 int
```

- `<T>` 同 C#；类型实参推断同 C#。
- 约束写在 `<>` 里，冒号后是类型；多个约束用 `and`。特殊约束 `class`、`struct`、`unmanaged`、`notnull`、`new()`。

### 泛型和空值（ADR-0031）

```nyxel
static func Find<T: class>(items: List<T>, pred: func(T) -> bool) -> T? {
    for x in items {
        if pred(x) { return x }
    }
    return null
}

class Registry<K: notnull, V> {                  // K 当 Dictionary 的键，不能是 null
    let entries: Dictionary<K, V> = new Dictionary<K, V>()
}

static func Find<T>(items: List<T>, ...) -> T?   // 错误：T 可能是 int，这时 T? 不表示“可能没有”
```

- 写 `T?` 时，约束里要看得出 T 是 class 还是 struct：`class` 或基类（`T: Enemy`）时 `T?` 是可能为 null 的引用；`struct` 或 `unmanaged` 时是 `Nullable<T>`。没约束、只有 `notnull` 或只有接口约束时写 `T?` 是编译错误。所以 `T?` 永远表示“可能是 null”，`return null` 照常写；Nyxel 没有 `default`。
- 实现或重写 .NET 成员时，签名里有没约束的 `T?`（`IComparer<T>.Compare(x: T?, y: T?)`）照签名写，含义同 C#。
- 调用 .NET 里返回没约束 `T?` 的方法同 C#：`list.FirstOrDefault()` 在 `List<int>` 上返回 int（没有时是 0），在 `List<Enemy>` 上返回 `Enemy?`。
- 没约束的 T 可以代入可空类型（`List<Enemy?>`），所以 T 的值可能是 null，调用它的成员前要先检查。
- `notnull`：T 可以是任何不可空的类型。有 `class`、`struct`、基类或接口约束的 T 本来就不能代入可空类型。
- 元素类型是 T 的数组要写 `element`，元素类型是 `T?` 的可以直接创建，见“数组”。

### 协变和逆变（ADR-0031）

- 只往外出 T 的接口（`IEnumerable<T>`、`IReadOnlyList<T>`）是协变的：`IEnumerable<Slime>` 可以当 `IEnumerable<Enemy>` 用。只往里进 T 的接口（`IComparer<T>`）是逆变的：`IComparer<Enemy>` 可以当 `IComparer<Slime>` 用。既进又出的（`List<T>`）两个方向都不行。
- 用 .NET 已经标好的接口和 `func(...)` 函数类型，规则同 C#：`let all: IEnumerable<Enemy> = slimes`、`let logSlime: func(Slime) = logAnyEnemy`。值类型不参与：`List<int>` 不能当 `IEnumerable<object>`。
- Nyxel 自己声明的接口暂时不能标 `in` / `out`（写 `interface ISource<out T>` 是编译错误）。要让 `ISpawner<Slime>` 传给需要刷怪点的函数，把函数写成泛型：`func Fill<T: Enemy>(spawner: ISpawner<T>)`。

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

struct enum Damage {                    // 带数据：写明 struct enum 或 class enum
    case Physical(Amount: int)
    case Burn(Amount: int, Seconds: float)
    case Heal(Amount: int)
}

let element = Element.Fire                              // 不带数据的 case：常量
let hit = new Damage.Burn(Amount = 12, Seconds = 3)     // 带数据的 case：new
```

- 每种情况一行，以 `case` 开头。
- 全部 case 不带数据时写 `enum`，等同 C# 的枚举。有带数据的 case 时必须写明放在哪（ADR-0039）：`struct enum` 是值类型，`class enum` 在堆上，见“带数据的 enum 放在哪”；光写 `enum` 是编译错误，诊断列出两种写法。文档里“带数据的 enum”泛指两种。
- case 的数据同元组（ADR-0024）：
  - 每一项都有名字，用 PascalCase。C# 那边是同名的属性（`hit.Amount`）。
  - 创建时按位置写（`new Damage.Burn(12, 3)`）或带名字写，同元组的值。
  - 用 `match` 按位置取出，同元组的解构（见“match”）。数据创建以后不能改。
- 判断是不是某个 case 一律写 `is`：`hit is Damage.Burn`、`state is SlimeState.Idle`，得到 bool，不取数据（ADR-0024、ADR-0040，见“类型检查与收窄”）。`x == 类型.Case` 是编译错误，诊断给出 `is`。
- 两个 enum 值之间用 `==`（ADR-0024）：带数据的（两种都是）比内容，同一个 case、数据逐个相等就相等，能当字典的键；不带数据的比值。

### enum 的成员与接口（ADR-0029）

```nyxel
enum Element {
    case Fire
    case Ice
    case Lightning

    public func Weakness(self) -> Element = match self {
        case Fire -> Element.Ice
        case Ice -> Element.Lightning
        case Lightning -> Element.Fire
    }

    public property IsHot: bool { get = self is Element.Fire }

    public static func Parse(text: string) -> Element? { ... }
}

struct enum Damage : IDescribable {         // 带数据的 enum：冒号后面是接口
    case Physical(Amount: int)
    case Heal(Amount: int)

    public func Describe(self) -> string = match self {
        case Physical(amount) -> $"{amount} 点物理伤害"
        case Heal(amount) -> $"治疗 {amount}"
    }
}

let w = element.Weakness()
let e = Element.Parse("ice")
```

- case 后面可以写实例方法、静态函数、计算属性（含 `static property`）。先写全部 case，再写成员。
- 不能写字段、`init`、自动属性、事件、索引器：enum 的值就是它的 case（加上 case 的数据），没有别的存储。
- 不能写 `var self`（值创建以后不能改），不能写 `protected`、`virtual`、`abstract`、`sealed`、`override`（enum 不能被继承）。
- 普通、flags、`struct enum`、`class enum` 四种都一样。不是自己定义的 enum（C# 和引擎的）照旧用扩展加方法。
- `struct enum` 和 `class enum` 能实现接口，接口的成员写在 enum 里；只能实现接口，不能继承类。
- 不带数据的 `enum` 不能实现接口：它是 C# 的 enum，C# 的 enum 不能实现接口。要实现就写成 `struct enum`（见“带数据的 enum 放在哪”）。
- C# 那边：不带数据的 enum 的成员在同一命名空间里生成的 `名字Extensions` 类的 extension 块里，C# 照样写 `element.Weakness()`、`Element.Parse("ice")`；`class enum` 的成员在抽象基类上，`struct enum` 的在 struct 上。
- 待定：带数据 enum 的 `ToString`。

### enum 的值与 flags（ADR-0024、ADR-0040）

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

- 只有不带数据的 `enum` 能写值、底层类型和 `flags`；`struct enum`、`class enum` 不能（ADR-0029）。
- 值：
  - 要么每个 case 都写，要么都不写。都不写时按声明顺序 0、1、2，同 C#。
  - 值是整数常量，可以用运算组合（`1 << 3`、`Layer.Ground | Layer.Water`）；引用别的 case 也写类型名。
  - 两个 case 不能是同一个值（ADR-0040）：case 是“几选一”，别名写成 `static let Old: Tile = Tile.New`。.NET 的枚举有别名照收，`match` 里两个别名都列出来是编译错误。
  - 存档、网络消息、和 C++ 引擎对齐的 enum 建议写明每个值：插入新 case 不会改掉旧的值。
- 底层类型写在冒号后面，同 C#：`byte`、`sbyte`、`short`、`ushort`、`int`、`uint`、`long`、`ulong` 之一，不写是 `int`。
- `flags enum`：
  - 每个 case 都写值，通常每个占一个二进制位，用 `|` 组合。检查包含用 .NET 的 `HasFlag`。
  - Nyxel 的 enum 只有 flags enum 能用 `|`、`&`、`^`、`~`；C# 定义的枚举照 C#，都能。
  - `match` 一个 flags enum（包括 C# 带 `[Flags]` 的）必须有 `case _`：组合出来的值列不全。
  - C# 那边是 `[Flags] enum`。`flags` 只在 `enum` 前面是关键字，别处照样能当名字。
- 没有值为 0 的 case 时，数组不能直接创建，要用 `element`（见“数组”）。flags enum 的 0 是“一个都没有”，总能直接创建。

### 带数据的 enum 放在哪：struct enum 与 class enum（ADR-0024、ADR-0039）

```nyxel
struct enum AiState {                   // 值类型：赋值时复制，不分配
    case Idle
    case Chasing(Target: Entity)
    case Fleeing(From: Entity, Until: float)
}

struct Brain {                          // ECS 组件：引擎要求 unmanaged
    var state: AiState = AiState.Idle
}

class enum Condition {                  // 引用类型：堆上的对象，可以递归
    case Leaf(Check: func() -> bool)
    case Not(Inner: Condition)
    case All(Items: List<Condition>)
}
```

- 带数据的 enum 必须写明放在哪，同 class 和 struct 之间没有默认：`struct enum` 是值类型，不在堆上分配，赋值时复制；`class enum` 的每个值是堆上的对象，赋值时共用。两种的写法和用法（创建、`match`、`is`、`==`）完全一样，只差在怎么存放。不带数据却写 `class enum` 是编译错误。
- `class enum` 用于递归的 case（行为树、表达式树）和数据很大的 case；其余用 `struct enum`。每次 `new 类型.Case(...)` 创建 `class enum` 的值都分配一次，状态机每帧切换的状态不要用它。
- `struct enum`：
  - 一个值里留着所有 case 的数据的位置（同类型的可以共用），大小约等于各 case 加起来。case 不能直接包含同一个 struct enum（要递归就用 `class enum`）。
  - 数据全是 unmanaged 类型时，整个 enum 也是，能放进要求 `T : unmanaged` 的 ECS 组件。
  - 全零的值是第一个 case，前提是它不带数据（上例是 `Idle`）；否则数组要用 `element` 创建，同 Nyxel 的 struct。
  - 当接口用时会装箱，同 struct。
- 两种冒号后面都是接口（ADR-0029），没有底层类型、case 的值和 `flags`。
- 不带数据的 enum 本来就是值类型，只有要实现接口时才写 `struct enum`（ADR-0029）；不带数据、也没写接口时写 `struct enum` 是编译错误。
  ```nyxel
  struct enum Element : IDescribable {
      case Fire
      case Ice

      public func Describe(self) -> string = ...
  }
  ```
  在 Nyxel 里用法和 enum 一样，大小也一样。代价：C# 那边是 struct，不是 enum（C# 的 `switch` case 标签、`Enum.GetValues`、`(int)e`、编辑器和序列化器按 enum 处理都用不了）；`Element.Fire` 不是常量，不能当参数的默认值；不能写值，存档和与引擎对齐的值用普通 enum。

### 构造

```nyxel
struct Point {
    public let X: float
    public let Y: float

    init(x: float, y: float) {                          // 不带名字的 init：最多一个；init 默认公开
        self.X = x
        self.Y = y
    }

    init FromPolar(radius: float, angle: float) {        // 命名 init
        self.X = radius * MathF.Cos(angle)
        self.Y = radius * MathF.Sin(angle)
    }
}

let a = new Point(x = 1, y = 2)
let b = new Point.FromPolar(radius = 1, angle = 0.5)
```

- 不带名字的 `init` 最多一个，其余必须有名字。构造函数不按参数类型重载。
- 没有默认值的字段，每个 `init` 结束前都必须赋过值（确定初始化检查）。
- `init` 默认公开（ADR-0040），和别的成员相反：构造函数十个里九个是公开的，自动生成的那个也公开。要藏起来写 `private init`（只给工厂函数用），只给子类用写 `protected init`。写 `public init` 是编译错误（ADR-0042）：和不写一样。`init` 里的 `self` 是正在构造的对象。
- C# 那边：不带名字的 `init` 是构造函数，命名 `init` 是同名静态方法（`Point.FromPolar(1, 0.5)`）。
- 没写 `init` 的类自动有一个公开的无参 `init`（ADR-0013），前提是每个字段都有初始值，否则编译错误。声明了任何 `init` 就不再自动生成；声明了 `init` 的 struct 不能写 `new Health()`。

### 基类与 super（ADR-0013）

```nyxel
public class Fireball : Projectile {
    public let Radius: float

    init(power: int, radius: float) {
        init super(power = power)           // 构造基类部分：必须是第一句
        self.Radius = radius
    }

    init Small() {
        init self(power = 5, radius = 0.5)  // 委托给本类的另一个 init
    }

    init Seeking(target: Entity) {
        init super.Homing(power = 8, target = target)   // 基类的命名 init
        self.Radius = 1
    }

    public override func Update(self, dt: float) {
        super.Update(dt = dt)               // 调用基类的同名方法
    }
}
```

- 调用其他 init 写 `init` 开头的语句（ADR-0027）：基类的写 `init super(...)`、`init super.名字(...)`，本类的写 `init self(...)`、`init self.名字(...)`。和创建对象对称：`new Projectile.Homing(...)` 创建一个完整的对象，`init super.Homing(...)` 构造自己的基类部分。
- 只能是 `init` 的第一句，参数里不能用 `self`。没写时自动调用基类的无参 `init`，基类没有就是编译错误。委托给本类 `init` 的不再调用基类的 `init`。
- `super.init(...)`、`self.init(...)`（Swift 的写法）是编译错误，诊断给出 `init super(...)`。
- 命名 init 不继承，同不带名字的 init：子类没声明 `Homing` 时，`new Fireball.Homing(...)` 是编译错误。
- 重写的方法里调用基类方法写 `super.方法名(...)`。
- C# 那边：`init super(...)` 是 `: base(...)`，`init self(...)` 是 `: this(...)`，`super.Update` 是 `base.Update`。命名 init 背后的构造函数对子类可见（protected），`init super.Homing(...)` 也是 `: base(...)`。

### new

- 创建值一律写 `new`：`new Type(...)`、`new Type.Name(...)`（命名 init 或带数据的 case）、`new List<Enemy>()`。
- 不带 `new` 的 `名字(...)` 一定是函数调用。

### 创建时设置成员（ADR-0027）

```nyxel
world.Commands.Add(entity, new Spin() with (
    Axis = Vector3.Normalize(axis),
    Speed = 0.3 + rng.NextSingle() * 1.7))

let psi = new ProcessStartInfo(fileName = "git")
    with (Arguments = "status", RedirectStandardOutput = true)
```

- `new 类型(实参) with (成员 = 值, ...)`：`()` 里是 init 的实参，`with ()` 里是构造完以后要设的成员。先执行 init，再按书写顺序赋值，同 C# 的对象初始化器 `new Spin() { Axis = ..., Speed = ... }`。
- 能设的：外面能访问的 `var` 字段、有 `set` 的属性、C# 的 `init` 访问器。`let` 字段和只读属性不能设。
- C# 类型的 `required` 成员必须写在 `with` 里，漏了是编译错误，诊断列出缺哪些。
- 每一项写 `名字 = 值`，同带名字的元组值。
- `with` 只能跟在 `new 类型(...)` 后面（`()` 不能省）。下一行以 `with` 开头时接着上一行，同 `.`。
- 没有 C# 的嵌套初始化器（`new A { B = { X = 1 } }`）和集合初始化器（用集合字面量）。C# 的 `new Enemy { HP = 10 }` 是编译错误，诊断给出 `with` 的写法。
- Nyxel 自己没有“只能在创建时设置”的成员（C# 的 `init` 访问器、`required`）：创建后不能改用 `let` 字段加 `init` 参数，必须给值由确定初始化检查保证。
- 待定：`with` 用在已有的值上（复制一份再改几个成员，同 C# 的 `with` 表达式）；Nyxel 自己声明只能在创建时设置的成员。

## 分支与离开语句（ADR-0025、ADR-0026）

`if` / `try` 当值用的分支、`match` 和 `await all` / `await any` 的分支、`??` 的右边能写什么，是同一套规则，各节不再重复：

- **离开语句**：`return`、`throw`、`break`、`continue`，`eval` 块里还有 `give`。
- **产生值的位置**（`->` 形式的 `if` / `try` 的分支、当值用的 `match` / `await` 的分支、`??` 右边）：一个表达式，或一条离开语句。写 `{ }` 块是编译错误；要先执行语句再给出值，写 `eval { ... give 值 }`。
- **当语句用的分支**（`match` / `await` 当语句用）：一条简单语句，或一个 `{ }` 块。简单语句指一行写完、不带块的语句：调用、`await`、`launch`、赋值（含 `+=`、`??=`）、`emit`、离开语句。`let`、`using`、循环、`if` / `try` 语句要写进块。
- `if` / `try` 当语句用时分支只能是 `{ }` 块，没有 `->`（见“if 与 eval”）。

## if 与 eval（ADR-0007、ADR-0025）

```nyxel
let label = if self.hp > 0 -> "alive" else -> "dead"

let speed = if self.IsFrozen -> 0
    else if self.IsHasted -> self.BaseSpeed * 2
    else -> self.BaseSpeed

if self.hp <= 0 {                   // 当语句用：分支写 { }
    self.Die()
} else {
    self.Regenerate()
}

let damage = eval {                 // 先执行几条语句，再给出值
    if self.isInvincible {
        give 0
    }
    let reduced = hit.Amount - self.armor
    give if reduced > 0 -> reduced else -> 0
}   // reduced 只在块里可见
```

- `{ }` 块本身没有值。用语句算出一个值只有 `eval` 加 `give` 这一种写法。
- `if` 当语句用时分支写 `{ 语句 }`；当值用时分支写 `-> 值`，必须有 `else`。看到 `if ... ->` 就知道它在产生值（`match` 的分支两种用法都写 `->`，是不是值看它写在哪）。
  - 写反是编译错误：`{ }` 形式写在要值的地方，或者 `->` 形式单独当语句。同一个 `if` 的分支也不能两种混用。
  - 当值用时，每个分支是一个表达式或一条离开语句（见“分支与离开语句”）。
  - 最后一个分支一直延伸到表达式结束，同 C# 的 `?:`：`if c -> a else -> b + 1` 的 else 分支是 `b + 1`。所以 `->` 形式的 `if` / `try` 当运算数，或者直接写在另一个 `if` / `try` 的分支里时要加括号（`else if` 连写不算）：`(if hasBonus -> 2 else -> 1) * base`、`if a -> (if b -> 1 else -> 2) else -> 3`。`=` 后面、参数、`return` 后面、`match` 分支里不用加。
- 没有 `?:` 三元运算符。
- 函数体必须用 `return` 返回值。

### eval 块（ADR-0025）

```nyxel
let text = eval {
    using stream = File.OpenRead(path)
    using reader = new StreamReader(stream)
    give reader.ReadToEnd()
}   // 文件在这里就关了

let target = eval {
    let candidates = self.Scan()
    if candidates.Count == 0 {
        give null
    }
    give candidates.MinBy(func(e) = e.HP)
} ?? return                         // 函数的出口留在顶层
```

- `eval { ... }` 是表达式，块里用 `give 值` 给出它的值。块里声明的名字（包括 `using`）只在块里可见；`using` 的资源在块结束时释放。
- 每条路径都以 `give` 或 `throw` 结束，走到块末尾还没给值是编译错误。最后一句不能是没有 `give` 的表达式（不像 Rust、Kotlin）。
- `give` 交给离它最近的 `eval`，可以穿过中间的 `if`、`match`、`try`、循环，但不能穿过 lambda。`eval` 外面写 `give` 是编译错误。`??` 右边也能写：`let t = self.Find() ?? give null`。
- 块里不能 `return`；`break` / `continue` 只能用于块里的循环，不能离开块。要在块里决定结束函数，就让块给出 `null`，在外面写 `?? return`。块里定义的 lambda 有自己的 `return`；`throw`、`await` 照常可用。
- `eval` 只能写在要值的地方，单独当一条语句是编译错误。
- 块的类型是所有 `give` 的值的共同类型，同 `if` 的各分支；有期望的类型（`let x: T = eval { }`）时按期望的类型。
- 翻译成 C#：先声明一个变量，块翻译成 C# 的块，每个 `give` 是赋值加 `goto` 到块后面。不是 lambda，不分配。

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
    case Physical -> self.hits += 1 // 当语句用：一条简单语句可以直接写
    case Heal -> return             // 离开语句也是
}

let scale = match self.size {
    case Small -> 0.5
    case Huge -> eval {             // 当值用：要先执行语句就写 eval
        self.PlayRumble()
        give 2.0
    }
    case _ -> 1.0                   // 默认分支
}
```

- 每个分支一行，以 `case` 开头。默认分支写 `case _`，`_` 匹配其余任何值（ADR-0026）；`else` 只属于 `if`，写在 `match` 里是编译错误。分支用 `->`（Nyxel 没有 `=>`）。
- `->` 右边（ADR-0025、ADR-0026）：
  - 当值用时是一个表达式或一条离开语句，当语句用时是一条简单语句或一个 `{ }` 块，见“分支与离开语句”。
- case 的数据按位置取出，同元组的解构（ADR-0024）：
  - 名字个数和数据项一样，`_` 丢弃不要的；名字自己起，成为分支里的局部变量。
  - 名字和数据项名是同一组（不分大小写）、顺序却不同时给警告：多半是写反了。
- 必须覆盖所有情况（当语句用也一样）：enum 列全所有 case 或写 `case _`；flags enum 和其他类型必须有 `case _`。“其余不处理”写 `case _ -> {}`。
- 模式（ADR-0010）：
  - 常量 `case 0`、`case "boss"`、`case null`；
  - 比较 `case < 10`；区间 `case 10..<50`、`case 1...3`；
  - 组合 `case 1 or 2`、`case > 0 and < 10`、`case not null`；
  - 类型 `case is Enemy`（被匹配的局部变量 / 参数在分支里收窄成该类型）；
  - 附加条件 `case Burn(_, seconds) when seconds > 3`。

## 空值（ADR-0009、ADR-0023、ADR-0041、ADR-0042）

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

- `T` 一定有值，`T?` 可能为 `null`，违反是编译错误。值类型的 `int?` 就是 C# 的 `int?`，规则和引用类型的 `T?` 一样。
- `T?` 用之前要检查：它没有成员（`int?` 的 `.Value`、`.HasValue`、`.GetValueOrDefault()` 也不能用，ADR-0041），不能传给要 `T` 的地方。
- C# 程序集的可空标注照单全收；没有标注的程序集，返回值、字段、属性当作 `T?`，参数可以接受 `null`。只管最外层：`List<Enemy>` 当作 `List<Enemy>?`，元素仍是 `Enemy`（ADR-0041）。
- 检查后自动收窄：只对局部变量和参数；字段、属性、调用结果先存进局部变量。
- 被 lambda 捕获的 `var` 局部变量不收窄（ADR-0042）：函数里只要有一个 lambda 读或写了它，它在整个函数里（lambda 里面也算）一直是声明的类型，因为 lambda 可能在检查和使用之间把它改掉。先存进 `let` 再用；`let` 和参数不能重新赋值，被捕获了也照常收窄。
  ```nyxel
  var target: Enemy? = self.FindTarget()
  slime.Died += func(s) { target = null }
  if target == null { return }
  target.Attack()                         // 错误：target 仍是 Enemy?
  let current = target ?? return          // 存进 let
  current.Attack()
  ```
- 给局部变量赋一个不可能是 null 的值（声明时的初始值也算），之后它当作非空；赋了可能是 null 的值，之前的检查作废（ADR-0041）。只管是不是 null：`var weapon: Weapon? = new Sword()` 之后 `weapon` 是 `Weapon`，类型的收窄只来自 `is`。
  ```nyxel
  var target: Enemy? = self.target
  if target == null {
      target = self.FindNearest()         // FindNearest 返回 Enemy
  }
  target.Attack()                         // 两条路径上都不是 null
  ```
- 没写类型的 `var` 的类型就是初始值的类型（ADR-0042）：`var current = node`（`node` 已经收窄成 `Node`）是 `Node`，之后 `current = current.next`（`Node?`）是编译错误，诊断给出 `var current: Node? = node`。
- 运算符里只有 `==` / `!=` 接受 `int?` 这样的值类型 `T?`，同 C#（ADR-0041）：两边都是 null 相等，一边是 null 不等，都有值时比值；`a != b` 永远是 `not (a == b)`。算术、大小比较、位运算要先检查：`(hp ?? 0) + 1`、`if hp != null and hp < 10`。
  - 注意 null 落在哪一边：`self.target?.IsDead != true` 在没有目标时也是 true（“不能确定已经死了”）；要“有目标而且活着”写 `self.target?.IsDead == false`。
- `?.`、`??` 同 C#；`??` 右边还可以是一条离开语句（ADR-0025，见“分支与离开语句”）。
- 和 `null` 比较只看引用是不是空的（ADR-0023）：`x == null` 是 C# 的 `x is null`，不调用类型重载的 `==`。所以 `== null`、`!= null`、`?.`、`??`、`case null`、收窄的意思都一样。两个值之间的 `a == b` 照常调用重载。“已销毁”这类状态用引擎提供的属性或方法查（`world.IsAlive(entity)`），不借用 null。
- 对不会是 null 的引用类型写 `== null`、`??`、`?.`、`??=` 是警告（ADR-0041）：类型说它一定有值，检查是多余的；和 C# 交界的地方（C# 调用 Nyxel 的 `public` 函数时可能传 null）照样可以写。不可空的值类型（`int`）上写是错误。
- `x ??= value`（ADR-0023）：`x` 是 null 时才赋值，同 C#，是语句。局部变量和参数之后当作非空；字段不收窄。
  ```nyxel
  name ??= "无名"                          // 之后 name 当作非空
  self.Target ??= self.FindNearestPlayer()  // 字段：只赋值
  ```
- 没有 `x!` / `x!!`；断言非空写 `?? throw new 异常("原因")`。
- 数组元素一开始是全零，所以 `new array<Enemy>(16)` 是编译错误，见“数组”。
- 泛型里的 `T?` 要求约束看得出 T 是 class 还是 struct，见“泛型”（ADR-0031）。

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

- `for 名字 in 集合`，循环变量不可变，每一轮是一个新的变量：lambda 捕获到的是这一轮的值，遍历区间时也一样。`while 条件 { }`。没有 C 风格的 `for`，没有 `do` / `while`。
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
- 异步遍历（`IAsyncEnumerable<T>`）写 `await for 名字 in 集合`，见“协程”（ADR-0034）。
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

## 运算符（ADR-0010、ADR-0042）

```nyxel
if not self.IsDead and target != null {
    self.Attack(target)
}
let canJump = self.onGround or self.coyoteTime > 0
self.combo += 1
```

- 逻辑运算用单词 `and`、`or`、`not`（短路、优先级同 C#）。没有 `&&`、`||`、`!`。
- 没有 `++` / `--`，写 `+= 1`。
- 算术、比较、位运算、复合赋值、整数除法、隐式数值转换同 C#，只有一处例外：整数相除的结果不能隐式变成浮点，见“整数相除的结果”（ADR-0042）。`??=` 见“空值”；Nyxel 的 enum 只有 flags enum 能位运算，见“enum 的值与 flags”。
- 赋值（包括 `+=` 等）是语句，不产生值：不能 `a = b = 0`，不能写在条件里（ADR-0011）。
- 优先级从高到低（ADR-0019）：成员访问、调用、下标 → 一元（`-` `+` `~` `not` `await` `launch`）→ `as` → `*` `/` `%` → `+` `-` → `<<` `>>` → `..<` `...` → `<` `>` `<=` `>=` `is` → `==` `!=` → `&` → `^` → `|` → `and` → `or` → `??`。除了 `as`（ADR-0014）和区间，其余同 C#。`??` 从右往左结合，区间不结合，其余从左往右。
  - `not` 和 C# 的 `!` 一样只管紧跟着的那个值：`not hp > 0` 是 `(not hp) > 0`，编译错误，诊断给出 `hp <= 0` 和 `not (hp > 0)`；`not x is T` 写 `x is not T`（ADR-0042）。
  - `&`、`^`、`|` 比比较低：`mask & 4 != 0` 是 `mask & (4 != 0)`，编译错误，诊断给出 `(mask & 4) != 0`（ADR-0042）。
- .NET 类型定义的运算符照 C# 用：`(a - b).Length()`、`matrix * other`。

### 自己的类型定义运算符（ADR-0030）

```nyxel
public struct Cell {
    public let X: int
    public let Y: int
    ...
    public operator +(a: Cell, b: Cell) -> Cell = new Cell(x = a.X + b.X, y = a.Y + b.Y)
    public operator *(a: Cell, k: int) -> Cell = new Cell(x = a.X * k, y = a.Y * k)
    public operator *(k: int, a: Cell) -> Cell = a * k
    public operator -(a: Cell) -> Cell = new Cell(x = -a.X, y = -a.Y)     // 一个参数：取负
    public operator <(a: Cell, b: Cell) -> bool = a.Y < b.Y or (a.Y == b.Y and a.X < b.X)
}

let next = cell + dir * 2
if next >= limit { ... }                // 由 < 生成
```

- `operator 运算符(参数) -> 返回类型`，体同函数（`= 表达式` 或块）。没有 `self`，两个操作数都是参数（静态的，同 C#）；参数至少一个是这个类型。
- 必须写 `public`，不写别的修饰符（`static` 也不写）。
- 能定义的：`+ - * / %`、`& | ^ << >>`，一元的 `-` `+` `~`，比较只有 `<`。参数个数：`~` 一个，`+` `-` 一个或两个，其余两个。
- 比较只写 `<`：`a > b` 是 `b < a`，`a <= b` 是 `not (b < a)`，`a >= b` 是 `not (a < b)`，三个都由编译器生成；同时生成 `IComparable<T>`，`list.Sort()`、`Max()` 直接能用，不能再写 `CompareTo`。`<` 返回 bool，只适合任意两个值都能比出大小的类型。
- 不能定义：`==` `!=`（见“相等”）、`>` `<=` `>=`、`and` `or` `not`（短路求值）、`??`、`is`、区间、复合赋值。`a += b` 用 `+`。
- 同一个运算符可以按操作数类型写几个（上例的两个 `*`）。
- 能写在 class、struct 和扩展里；enum 和接口里暂时不能写。参数不能有默认值，不能是 `out` / `ref`，可以是 `in`。
- C# 那边是 `public static Cell operator +(Cell a, Cell b)`，`<` 生成全部四个比较运算符。C# 的写法是编译错误，诊断给出 Nyxel 的写法。
- 待定：enum 和接口里的运算符；自定义复合赋值（C# 14 的 `operator +=`）。

### 相等（ADR-0030）

```nyxel
if cell == goal { ... }                  // struct：X、Y 都相等就相等
let walls = new HashSet<Cell>()          // 能当字典的键，不装箱
if target == self.player { ... }         // class：是不是同一个对象
```

- class 比是不是同一个对象；struct、enum（ADR-0024）、元组比内容。`==` 和 `!=` 不能自己定义。
- struct 的字段逐个用 .NET 的 `Equals` 比，同 C# 的 `record struct`：class 类型的字段比引用（两个内容一样的 `List` 不相等），float 字段两个 NaN 算相等。
- 编译器给 struct 生成 `IEquatable<T>`、`Equals`、`GetHashCode`、`==`、`!=`，C# 那边也能用。struct 和 enum 不能自己写 `Equals`、`GetHashCode`；要别的比较方式就写普通方法。
- class 可以照 C# 重写 `Equals` / `GetHashCode`（影响字典和 `Contains`），`==` 仍然比引用。
- 和 `null` 比较见“空值”。
- 待定：struct 自定义相等。

## 类型检查与收窄（ADR-0010、ADR-0024、ADR-0040）

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
- 被检查的是局部变量或参数时自动收窄（同空值收窄的规则，被 lambda 捕获的 `var` 不收窄），不另起变量名。
- `x is 类型.Case` 判断是不是某个 enum case（ADR-0024）：`if self.state is SlimeState.Idle`、`hit is not Damage.Heal`。只判断，不取数据，也不收窄；要数据写 `match`。`==` 右边直接写 case 名（`state == SlimeState.Idle`、`key == Key.Escape`）是编译错误，诊断给出 `is`（ADR-0040）；两个 enum 值之间照常 `==`。

## 类型转换（ADR-0014）

```nyxel
let cells = self.distance as int                 // 向零截断：2.7 → 2，-2.7 → -2
let percent = self.current as float / self.Max   // 先转换再除，不是整数除法；不写 as 赋给 float 是错误
let index = MathF.Round(self.t * 10) as int      // 要四舍五入就先 Round
let code = Element.Fire as int                   // 枚举和整数互转

let p = other as Projectile                      // 错误：可能失败，用 if other is Projectile
```

- `as` 是转换，不是类型检查，不会因为“类型不对”而失败。允许：数字之间、枚举和整数之间、子类转父类或接口、C# 类型定义的显式转换运算符、Nyxel 类型用 `operator as` 声明的转换（ADR-0030）。
- 可能失败的向下转换写 `as` 是编译错误，用 `is` 收窄。
- 语义同 C# 的强制转换：向零截断，不检查溢出，常量超出范围是编译错误。整数转成 enum 也不检查：`5 as Element` 可能不是任何 case，列全了 case 的 `match` 遇到它时抛异常，同 C# 的 switch 表达式（ADR-0024）。
- 优先级比 `*` `/` 高，比一元运算符和成员访问低。
- 没有 C# 的 `(int)x` 写法。
- C# 类型定义的隐式转换同 C#，在需要时自动发生（ADR-0030）：参数是 `ReadOnlySpan<char>` 的 .NET 方法直接传 string。Nyxel 自己不能定义隐式转换。

### 自己的类型定义转换（ADR-0030）

```nyxel
public struct Fixed {
    let raw: int
    ...
    public operator as(value: int) -> Fixed = new Fixed.FromRaw(value << 16)
    public operator as(value: Fixed) -> float = value.raw as float / 65536
}

let speed = 3 as Fixed
let shown = speed as float
```

- `operator as(值: 源类型) -> 目标类型`，用 `值 as 目标类型` 调用。用于同一个量换一种表示，创建新东西照样 `new`。
- 规则同运算符：必须 `public`，可以按源类型写几个。源和目标有一个是这个类型，不能转成自己的基类或接口；扩展里不能写（C# 14 不支持）。
- C# 那边是 `explicit operator`，C# 代码写 `(float)speed`。

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

let config = try -> Config.Load(path) catch e: IOException -> Config.Default

let level = try -> Level.Load(name)
    catch e: FileNotFoundException -> return
    finally { self.loading = false }
```

- 用 .NET 的异常。`catch 名字: 类型`，名字必须写；用不到异常对象时写 `catch _: IOException`（ADR-0026）；捕获所有异常写 `catch e: Exception`。`when` 过滤同 C#。
- 被前面的 `catch` 完全覆盖、永远到不了的 `catch` 是编译错误。`try` 后面至少有一个 `catch` 或 `finally`。
- `{ }` 形式的 `} catch`、`} finally` 跟在 `}` 后面同一行。
- `try` 当语句用时分支写 `{ }`；当值用时分支写 `->`，规则同 `if`（ADR-0025，见“分支与离开语句”）：`catch`、`finally` 可以写在下一行开头；`finally` 不产生值，照旧写 `finally { 语句 }`。
- `throw 异常对象` 是语句。在 `catch e` 里 `throw e` 重新抛出同一个异常，保留原来的调用栈（翻译成 C# 的 `throw;`）。
- `finally` 里不能 `return`。
- 文档注释里写会抛的异常：`- Throws 类型: 说明`（ADR-0035，见“注释”）。

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
- `using` 只有这一种用法：导入是 `import`，没有块形式的 `using (...) { }`，没有 `defer`。要更早释放，就把用到资源的语句放进 `eval` 块（块结束时释放，ADR-0025）或拆成函数；和 `IDisposable` 无关的清理用 `try` / `finally`。
- 异步释放（`IAsyncDisposable`）写 `await using 名字 = 值`，见“协程”（ADR-0034）。

## 协程（ADR-0016、ADR-0034）

```nyxel
import Nyxel                            // Wait、Coroutine 在 Nyxel.Runtime 里

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

let loading = launch self.Load(name = "Forest")
let level = await loading               // 之后再等结果
```

- `async func` 声明会跨帧的函数，可以有返回值（`-> R`）。lambda 写 `async func(e) { }`，函数类型写 `async func(A) -> R`。重写时 `async` 必须和基类一致。不能有 `out` / `ref` 参数。
- `await` 只能写在 `async` 函数里，能等 Nyxel 的协程、.NET 的 `Task` / `ValueTask`、任何有 `GetAwaiter` 的对象。恢复后的代码在主线程、下一次调度时执行。
- 结果可以等待的调用（`async` 函数、返回 `Task` 的 .NET 方法、`Wait` 系列）只能直接写在 `await` 或 `launch` 后面，否则编译错误。
- `launch 调用` 得到协程的句柄 `Coroutine`（有结果的是 `Coroutine<T>`，C# 那边 `async` 函数的返回类型也是它）：`loading.Stop()` 停止，`await loading` 等它完成。
- `launch obj.方法()` 的协程绑定在 `obj` 上：`obj` 被引擎销毁后，下一次恢复时自动停止。静态函数和没有生命周期的对象启动的协程不绑定。
- 停止时 `finally`、`using`、`await using` 的释放照常执行，`catch` 接不住。清理里的 `await` 照常等到完成，不会因为绑定对象已经销毁而再停一次。`await` 一个被停止的协程，等待方也停止。
- `launch` 出去的协程里没被接住的异常交给宿主记日志。
- 热重载前所有协程都会停止。
- 等待函数（Nyxel.Runtime，`Nyxel` 命名空间）：
  - `Wait.Seconds(s)` 按游戏时间计，也就是宿主每帧传进来的时间，游戏暂停时不走。按真实时间计的版本和宿主接口一起定（M4）。
  - `Wait.NextFrame()`、`Wait.Frames(n)`、`Wait.Until(func() = 条件)`。没有 `Wait.While`，写 `Wait.Until(func() = not ...)`。
- 待定：等一个事件触发（GDScript 的 `await signal`）。

### 同时等多个（ADR-0034）

```nyxel
await all {                                  // 都完成才继续
    case self.Camera.PanTo(target)
    case self.Dialog.FadeIn()
}

await any {                                  // 先完成的一项胜出，其余的停止
    case let key = Input.WaitForAnyKey() -> self.Answer(key)
    case Wait.Seconds(10) -> self.Answer(Key.None)
}

await any {                                  // 一边做一边抖屏，蓄力结束就停
    case self.Boss.Charge()
    case self.Camera.ShakeForever()
}

let dodged = await any {                     // 当值用，规则同 match
    case self.WaitForKey(Key.E) -> true
    case Wait.Seconds(2) -> false
}

let (tex, sfx) = await all {                 // 每一项的值按顺序组成元组
    case let t = Assets.LoadTexture("slime") -> eval {
        self.loading.Step(name = t.Name)     // 这一项完成就推进度条
        give t
    }
    case Assets.LoadSound("slime")
}
```

- 写法：
  - 每一项写成 `case [let | var 名字 =] 能等待的表达式 [-> 分支]`，一项一行，至少两项。`all`、`any` 只在 `await` 后面紧跟 `{` 时是关键字。
  - 每一项是调用（`async` 函数、返回 `Task` 的 .NET 方法、`Wait` 系列）或已有的句柄，不写 `await`。不能给已有的变量或字段赋值（`case self.texture = Load()` 是错误），要存就在分支里或块后面赋值。
  - `case let` / `case var` 同普通的 `let` / `var` 声明，可以写类型、解构。名字只在这一项的分支里可见，块后面用不到，所以起了名字就要写分支。
  - 分支同 `match`（见“分支与离开语句”）。
- 运行：
  - 各项从上到下依次开始，然后同时进行。它们和 `await 调用` 一样属于当前协程（绑定当前协程的对象），没有自己的句柄，不会被单独停掉。
  - `all`：每一项完成后执行它的分支，其他项可能还在跑。所有项连同分支都结束后才继续。一项抛异常时，其余的停止，异常从 `await all` 抛出。
  - `any`：一项先完成或抛异常时，其余的停止（`finally`、`using` 照常执行），然后执行胜出那一项的分支，或者抛出它的异常。
  - 当前协程被停止时，块里的全部停止。块只停自己启动的：已有的句柄、.NET 的 `Task` 输了只是不再等它。
- 当值用：
  - 每一项的值：写了 `->` 是分支的值，没写就是这一项本身的结果。
  - `any` 的值是胜出那一项的值，各项类型要一致。
  - `all` 的值是所有项的值按顺序组成的元组：直接解构，或者有期望的类型（同按位置写的元组值）。每一项都要有值，本身没有结果的项要写 `-> 值`。
- 要拿结果、又要各自独立运行（各自绑定、能单独停止）时，用 `launch` 加 `await` 句柄：`let tex = launch Assets.LoadTexture("slime")`，之后 `await tex`。
- 每一项是一个子协程，启动时要分配，同 `launch`。用在流程（过场、加载、QTE）里，不放在每帧遍历实体的循环里。

### 异步释放和遍历（ADR-0034）

```nyxel
async func Upload(self, path: string) {
    await using conn = await Server.ConnectAsync(self.address)
    await conn.SendAsync(File.ReadAllBytes(path))
}   // 离开时等 conn.DisposeAsync() 完成

async func Pump(self, session: RuntimeSession) {
    await for e in session.Events.ReadAllAsync() {
        self.Apply(e)
    }
}
```

- 都只能写在 `async` 函数里。会暂停的语句一律以 `await` 开头。
- `await using 名字 = 值`：值实现 `IAsyncDisposable`（或有 `DisposeAsync`，同 C#），其余同 `using`。只实现了 `IAsyncDisposable` 的写成 `using` 是编译错误；两个都实现的（`FileStream`）两种都能写，各调各的。
- `await for 名字 in 集合`：遍历 `IAsyncEnumerable<T>`（或有 `GetAsyncEnumerator`，同 C#），可以解构。JS、Swift 的 `for await` 是编译错误。

## 属性（ADR-0011）

```nyxel
class Player : Behaviour {
    var hp: int = 100                       // 字段

    public property IsDead: bool { get = self.hp <= 0 }     // 计算属性：getter 可以写表达式体

    public property HP: int {
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
- 访问器要么都带函数体（计算属性），要么都不带（自动属性，初始值写在类型后面）。每个访问器一行，可以单独加可见性（`private set`；`private` 只出现在访问器和 `init` 上）。
- getter 的函数体可以是块，也可以是 `= 表达式`，同函数（ADR-0026）。setter 的体是语句，只能写块。
- setter 的新值在括号里命名：`set(value)`。访问器里 `self` 隐式可用。静态属性写 `static property`。
- 默认用字段；实现接口的属性、对外只读对内可写、值需要计算时用属性。
- C# 的 `init` 访问器（只能在创建时设置）用 `with` 设，见“创建时设置成员”（ADR-0027）；Nyxel 自己暂不能声明。
- 没有 C# 14 的 `field` 关键字（ADR-0029）：访问器仍然要么都带体，要么都不带。setter 里要加检查，就写字段加计算属性（上例的 `hp` 和 `HP`）。

### 索引器（ADR-0029）

```nyxel
public class Grid<T> {
    let width: int
    var cells: array<T>
    ...
    public property self[x: int, y: int]: T {
        get = self.cells[y * self.width + x]
        set(value) { self.cells[y * self.width + x] = value }
    }
}

grid[3, 4] = Tile.Wall
let t = grid[0, 0]
```

- 名字的位置写 `self[参数]`，其余同属性：`get = 表达式`、`set(value) { }`、`private set`，只写 `get` 是只读的。接口里写 `property self[index: int]: T { get }`。
- 访问器必须带体，没有自动索引器，也不能写初始值。
- 参数写法同函数参数，至少一个；不能有默认值，不能是 `out` / `ref`。
- 一个类型最多一个索引器（同函数不能重载）；实现接口或重写要求的除外。
- 不能 `static`；扩展里暂时不能声明。
- C# 那边是 `this[...]` 索引器。

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
- `emit 接收者.事件(实参)` 触发，只有声明事件的类型能 `emit`（子类也不行）。处理函数按订阅顺序同步执行。一个处理函数抛的异常交给宿主记日志，接着通知下一个，`emit` 的一方不受影响（ADR-0034）；C# 写的处理函数、Nyxel 订阅的 C# 和引擎事件也一样。
- `+=` / `-=` 订阅和取消，C# 和引擎的事件也一样。
- 订阅绑定在“执行谁的代码”上：`对象.方法` 绑定那个对象，lambda 绑定写它的 `self`。绑定对象被引擎销毁后，订阅在下一次触发时自动移除；热重载时脚本做过的所有订阅都会移除。对 lambda 写 `-=` 是编译错误。
- C# 那边是普通的 C# `event`。
- 事件没有访问器（C# 的 `add` / `remove`，ADR-0029）。要转发别的对象的事件，就订阅它，在处理函数里 `emit` 自己的事件。
- 实现接口里的事件、重写基类的事件时，用接口或基类规定的委托类型，参数的类型和顺序必须和它一致，名字自己起（ADR-0029）：
  ```nyxel
  public class Inventory : INotifyPropertyChanged {
      public event PropertyChanged(sender: object?, e: PropertyChangedEventArgs)
  }
  ```
- 处理函数不是 `async`（`+= async func(s) { }` 类型不匹配）。要跨帧就在里面 `launch`：`slime.Died += func(s) { launch self.RespawnLater(s) }`。

## 状态机（ADR-0017）

没有专门语法：状态用 `struct enum` 写，每种状态需要的数据放在 case 里，在 `Update` 里 `match` 当前状态；按顺序推进的流程用协程。

```nyxel
struct enum SlimeState {
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
- 状态写成 `struct enum`（ADR-0024、ADR-0039）：切换状态不在堆上分配，数据全是 unmanaged 时还能放进 ECS 组件。

## 可见性（ADR-0006、ADR-0008、ADR-0040、ADR-0042）

- 成员默认私有；公开写 `public`，只给子类用写 `protected`（ADR-0015），含义与 C# 相同。struct 的成员不能是 `protected`。成员上写 `private` 是编译错误（ADR-0026）：它只出现在访问器（`private set`）和 `init` 上（`init` 默认公开，ADR-0040）。反过来，`init` 上写 `public` 也是编译错误（ADR-0042）。
- 类型默认只在本程序集可见；给其他程序集用写 `public`。
- 重写时可见性必须和基类一致：基类是 `protected virtual` 就写 `protected override`。
- 没有 `internal`，也没有 C# 的 `protected internal`、`private protected`。
- 修饰符的顺序固定（ADR-0019）：可见性（`public` / `protected`；访问器和 `init` 上还有 `private`，`init` 上不写 `public`）→ `static` → `abstract` / `sealed` / `virtual` → `override` → `async`。顺序不对、重复都是编译错误，诊断给出正确的写法：`public static var`、`public static func`、`protected override func`、`public override async func`、`sealed override`。

## 命名（ADR-0006、ADR-0035）

编译器检查，不符合给警告。

| 什么 | 写法 | 例 |
|---|---|---|
| 类型、方法、属性、`public` / `protected` 字段、命名 init、enum case 和它的数据、元组元素 | PascalCase | `EnemySpawner`、`TakeDamage`、`SpawnInterval`、`FromPolar`、`Burn`、`Seconds`、`Min` |
| 参数、局部变量、非公开字段 | camelCase，不加前缀 | `amount`、`spawnPoint`、`hp` |

缩略词同 .NET 的命名规范（ADR-0035）：两个字母的全大写，三个字母以上的只大写首字母；camelCase 开头的词整个小写。`Id`、`Ok` 是缩写，不是缩略词，写 `Id`、`Ok`。

| PascalCase | camelCase |
|---|---|
| `PlayerUI`、`IOError`、`MaxHP`、`EnemyAI` | `uiRoot`、`ioError`、`hp`、`maxHP` |
| `XmlReader`、`JsonText`、`NpcSpawner`、`Hud` | `xmlText`、`npcCount`、`hud` |
| `UserId`、`OkButton` | `id`、`userId` |

- 命名检查能查出三个以上大写字母连成的词（`XMLReader` → `XmlReader`、`ReadJSON` → `ReadJson`）和 `ID`、`OK`（`UserID` → `UserId`）。`Hp` 和 `HP` 哪个对查不出来：编译器不知道一个词是不是缩略词，按规则写 `HP`。
- 重写的成员、实现接口的成员不查，名字跟着基类。调用 .NET API 时名字原样使用。

## 待定

大的主题都讨论过了。剩下的细节记在各节末尾的“待定”里，写 samples 或实现时遇到再逐个讨论。还没定的写法一律是编译错误：以后放开不会让已有代码失效。诊断见 design/diagnostics.md。

不属于任何一节的：特性（C# 的 `[Serializable]`、`[Obsolete]`）还没讨论过，roadmap 记在 M3；取类型对象和成员名字（C# 的 `typeof(T)`、`nameof(x)`）和它一起讨论（ADR-0042）。
