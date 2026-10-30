# ADR-0033: 同名类型同 C#、命名空间别名 `import X under Pb`、没有静态导入

- 状态：已接受
- 日期：2026-10-31

## 背景

第二十六轮讨论，“待定”的第七轮：导入。ADR-0012 留下了导入别名和静态导入（C# 的 `using static`）。讨论时加了一项直接相关的：两个命名空间里有同名类型时怎么办。别名主要就是为它准备的，这条规则 ADR-0012 没有写。

查了 EnginePlayground 的 170 个 .cs（不算 SDK 生成的 `global using`）：

- `using static` 一个都没有。
- 别名只有一个：editor/Editor.Ipc/Messages.cs 的 `using Pb = ep.protocol;`。这个文件自己定义了 `LogLevel`，对应协议生成代码里的 `ep.protocol.LogLevel`，用后者时写 `Pb.LogLevel`，全文 63 处。

用 Roslyn 5.9（C# 14）验证过：

- 导入的两个命名空间都有 `Random`：只导入不报错，用到时报 CS0104。
- 自己所在的命名空间里有同名类型时，自己的优先：`Messages.cs` 的情形里，`LogLevel` 是 `Editor.Ipc.LogLevel`。
- 别名不导入名字：只有 `using Pb = ep.protocol` 时，单写 `LogLevel` 是 CS0246。
- 别名和自己命名空间里的类型同名时不报错，自己的类型悄悄盖过别名。
- 类型别名可以是封闭的泛型（`Dictionary<(int X, int Y), int>`），不能是 `List<>`（CS7003）。

前两项按推荐定。别名的写法先推荐了 C# 的 `import Pb = ep.protocol`，用户不喜欢这种写法，提出 `using Pb = import ep.protocol`；比较了 `as`、`alias`、`namespace Pb = ...` 等写法之后，用户又提出 `to` 和 `under`，选了 `under`。

## 决策

1. **同名类型同 C#。**

   ```nyxel
   import System          // System.Random
   import Engine          // Engine.Random

   let rng = new Random()          // 错误：两个命名空间都有 Random；写 System.Random，或者给命名空间起别名
   let rng = new System.Random()   // 能
   ```

   - 导入的两个命名空间有同名类型时，只导入不报错，用到这个名字时是编译错误。诊断列出两个全名，给出写全名和起别名（决策 2）两种改法。
   - 自己所在的命名空间（和外层）里的类型优先于导入的，不报错：“近的优先”。以后引擎或库新加一个和自己的类型同名的类型，自己的代码不受影响。

2. **只能给命名空间起别名，写 `import 命名空间 under 别名`。**

   ```nyxel
   import ep.protocol under Pb

   public enum LogLevel : byte { ... }          // 自己的

   let level = Pb.LogLevel.Warn                 // 协议里的
   ```

   - 用的时候写 `别名.类型名`，类型名本身不变，读者看到的还是 `LogLevel`，只是多了一个前缀说明它来自哪里。
   - 别名只引入这一个名字，不导入命名空间里的类型：单写 `LogLevel` 不会找到 `ep.protocol.LogLevel`。`under` 的字面意思“放在 Pb 下面”说的正是这个。要直接写名字，另写 `import ep.protocol`。
   - 不能给类型起别名（`Vec3` 代表 `System.Numerics.Vector3`）：`under` 前面写的是类型时是编译错误。
   - 别名只在这个文件里有效。同一个文件里两个导入用了同一个别名是编译错误（NYX1227）。别名和这个文件里看得见的类型或命名空间同名也是编译错误，比 C# 严：C# 里自己命名空间的同名类型会悄悄盖过别名。
   - `under` 只在导入行的命名空间后面是关键字，别处照样能当名字，同 `flags`。
   - C# 的 `using Pb = ep.protocol`、写错的 `import Pb = ep.protocol`、Python 和 Kotlin 的 `import ep.protocol as Pb` 都是编译错误（NYX1225），诊断给出 `import ep.protocol under Pb`。
   - 翻译：C# 的 `using Pb = ep.protocol;`。

3. **没有静态导入。**

   ```nyxel
   let len = MathF.Sqrt(x * x + y * y)     // 永远写类型名
   ```

   - ADR-0008 决策 5：不带接收者的名字只能是局部变量或参数。看到 `Sqrt(x)` 就知道 `Sqrt` 是局部变量，不用去翻导入。
   - enum 不需要：`match` 里的 case 本来就不写类型名（`case Burn(amount, _)`），`match` 外面写 `Element.Fire`。
   - C# 的 `using static System.MathF`、Java 的 `import static` 是编译错误（NYX1226），诊断说明要在成员前面写类型名。

## 备选方案

- 同名类型：
  - **自己的类型和导入的同名也报错**：读者不会搞错 `LogLevel` 是哪一个，但库新加一个同名类型，原来没问题的代码就编译不过；`Messages.cs` 这种自己定义同名镜像类型的写法处处要加前缀。
- 别名的范围：
  - **命名空间和类型都能起别名**（C#）：长的泛型类型写一次就够，但代码里出现看起来像类型、其实是别名的名字（`Grid`、`Vec3`），各文件可能给同一个类型起不同的别名，Agent 也容易自己编一个。
  - **不支持别名，冲突时写全名**（Java）：规则最少，但 `Messages.cs` 那 63 处都是 `ep.protocol.LogLevel`。
- 别名的写法：
  - **`import Pb = ep.protocol`**（C# 的 `using` 换成 `import`，原来的推荐）：用户不喜欢 `import` 后面紧跟 `别名 =` 的样子。
  - **`using Pb = import ep.protocol`**（用户提出）：名字在左边像声明，但 `using` 在 Nyxel 里是释放资源（ADR-0015），`using reader = new StreamReader(stream)` 和它长得一样；右边的 `import ep.protocol` 看起来像能算出值的表达式。
  - **`import ep.protocol as Pb`**（Python、Kotlin、Rust、TypeScript）：认识的人最多，但 `as` 在 Nyxel 里是类型转换（ADR-0014）；`import System` 导入名字、`import X as Pb` 却不导入，字面上看不出来。
  - **`alias Pb = ep.protocol`**：字面上不会让人以为导入了名字，但多一个词，依赖也不全以 `import` 开头。
  - **`namespace Pb = ep.protocol`**（C++）：和文件开头声明自己命名空间的 `namespace Game.Enemies` 容易看混。
  - **`let Pb = import ep.protocol`**（JS 的 `const fs = require("fs")`）：`let` 声明的是值，命名空间不是。
  - **`import ep.protocol to Pb`**（用户提出）：英语里像“导入到 Pb 里去”，Pb 像是一个已有的容器；`to` 也常让人想到“转换成”，Scala 里 `1 to 10` 是区间。
  - 选 `under`：每行依赖都以 `import 命名空间` 开头；字面意思和 `Pb.LogLevel` 的用法对得上；`as` 只保留转换一个意思。代价是没有别的语言这么写，Agent 第一次多半写 `as` 或 `using`，要靠诊断改过来。
- 静态导入：
  - **同 C#，`import static System.MathF`**：数学多的代码短一些，但要改写 ADR-0008 决策 5，读者看到不带前缀的名字要去翻导入。

## 后果

- 没有修改旧决定。ADR-0012 的待定由本 ADR 定了。
- 语法树：`ImportDirectiveSyntax.UnderKeyword` 和 `Alias`。`under` 是标识符记号，`import x as y` 报错后 `as` 留在 `UnderKeyword` 的位置（同 `using System` 的 `using` 留在 `ImportKeyword`）。别名写在前面的（`using Pb = ...`）和静态导入报错后整行跳过。新错误码 NYX1225–NYX1227。
- TextMate 语法只在导入行给 `under` 上色。
- 留给语义分析（M2）：同名类型的查找顺序和 CS0104 那样的诊断；`under` 前面是不是命名空间；别名和看得见的类型、命名空间重名。
- 样例 samples/Common/TeamMapping.nyxel：自己的 `Team` 和协议生成代码里的 `Wire.Team`。
