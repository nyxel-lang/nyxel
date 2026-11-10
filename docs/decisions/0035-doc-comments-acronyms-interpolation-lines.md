# ADR-0035: 文档注释的参数、返回值和异常，文档里的链接，缩略词的大小写，插值不跨行

- 状态：已接受
- 日期：2026-10-31

## 背景

第二十八轮讨论，“待定”的第九轮：约定。前几轮留下的三项：

- ADR-0007 定了文档注释写 Markdown、编译器转成 .NET 的 XML 文档，参数和返回值的写法（对应 XML 的 `<param>` / `<returns>`）留到以后，倾向 Swift 的 `- Parameter name:` / `- Returns:`。ADR-0014 留了会抛哪些异常的写法。
- ADR-0006 的命名规则没说缩略词（UI、HP、XML、ID）怎么写。
- ADR-0020 留了插值的 `{ }` 里能不能换行：C# 11 起允许，Nyxel 目前不允许。

讨论时加了一项：文档里怎么链接到别的代码（C# 的 `<see cref>`）。它和参数的写法一起决定 `///` 怎么转成 XML 文档，分开定容易冲突。

C# 项目引用 Nyxel 程序集时，IntelliSense 显示的是 XML 文档：补全列表里是 `<summary>`，调用时提示 `<param>`，悬停显示 `<returns>`、`<exception>`，`<see cref>` 能点过去。Markdown 里要有固定的写法对应这些标签。

四项都按推荐定。

## 决策

1. **参数、返回值、会抛的异常写成 Markdown 列表项 `- Parameter 名字:`、`- Returns:`、`- Throws 类型:`**，放在文档注释最后：

   ```nyxel
   /// 从文本读地图。
   ///
   /// 第一行是 `map: 名字`；其余每行一排格子：`#` 是墙，`.` 是地面，`$` 是放着金币的地面。
   ///
   /// - Parameter text: 整个地图文件的内容。
   /// - Returns: 读出来的地图，宽度按第一排格子定。
   /// - Throws ArgumentOutOfRangeException: 第一行比 `map: ` 还短时。
   public static func Parse(text: string) -> TileMap {
   ```

   - 写法：
     - 每个参数一行 `- Parameter 名字: 说明`。不用 Swift 的分组写法（`- Parameters:` 下面缩进列出每个参数）：一件事一种写法。
     - 函数、`init`、事件、索引器的参数都这样写。泛型参数也写 `- Parameter T:`，同 Kotlin 的 `@param T`：类型参数和普通参数不能同名，看名字就知道是哪一种。
     - `- Throws` 后面写异常的类型，写法同代码里的类型名。Swift 的 `- Throws:` 只有说明，Nyxel 要生成 `<exception cref>`，所以加上类型，形状和 `- Parameter 名字:` 一样。
     - 顺序：Parameter（按声明的顺序）、Returns、Throws。都不是必须写的，名字和类型已经说清楚时就不写。
     - `Parameter`、`Returns`、`Throws` 是固定的英文词，说明用什么语言写都行。
   - 和 C# 一样，编译器不检查函数是不是真的会抛这个异常：.NET 的元数据里没有“会抛什么”（ADR-0014 不加 Swift 的 `throws`），这只是文档。
   - 转成 XML 文档：
     - 第一段是 `<summary>`，也就是补全列表里显示的那一句；其余段落是 `<remarks>`。
     - 列表项是 `<param>`、`<typeparam>`、`<returns>`、`<exception cref>`。
   - 警告：参数名不存在，同一个参数写了两次，函数没有返回值却写了 `- Returns:`，`- Throws` 后面没写类型或者写的不是异常类型。写成 C# 的 `<param>` / `<returns>`、Javadoc 的 `@param`、Swift 的分组写法时，警告并给出这里的写法。

2. **文档里链接到别的代码，写成方括号里一个代码片段 `` [`TileMap.Parse`] ``**（Rust 的写法）：

   ```nyxel
   /// 这一格是不是墙，地图外面也算墙。地图由 [`TileMap.Parse`] 读进来，[`x`] 是列，从 0 开始。
   public func IsWall(self, x: int, y: int) -> bool = self[x, y] is Tile.Wall
   ```

   - 名字的写法同代码：类型直接写名字，成员写 `类型.成员`，因为代码里不带接收者的名字只能是局部变量或参数（ADR-0008）。参数和泛型参数直接写名字。
   - 转成 XML：类型和成员是 `<see cref>`，参数是 `<paramref>`，泛型参数是 `<typeparamref>`。悬停时能点过去。
   - 链接不到的名字给警告：改名以后，旧文档里的名字不会悄悄过时。
   - 只有反引号的 `` `x` `` 是普通的代码片段（转成 `<c>`），不当链接。

3. **缩略词的大小写同 .NET 的命名规范**：两个字母的全大写，三个字母以上的只大写首字母；camelCase 开头的词整个小写。`Id`、`Ok` 是缩写，不是缩略词，写 `Id`、`Ok`（.NET 的规范专门写了这一条）。

   | PascalCase | camelCase |
   |---|---|
   | `PlayerUI`、`IOError`、`MaxHP`、`EnemyAI` | `uiRoot`、`ioError`、`hp`、`maxHP` |
   | `XmlReader`、`JsonText`、`NpcSpawner`、`Hud` | `xmlText`、`npcCount`、`hud` |
   | `UserId`、`OkButton` | `id`、`userId` |

   - Nyxel 的代码总是和 .NET 的名字写在一起（`System.IO`、`IOException`、`XmlReader`、`JsonSerializer`），规则相同，读者看到的是同一种风格。Agent 写 C# 时用的也是这一套，Kotlin 的规范也一样。
   - 命名检查（ADR-0006，警告）能查出：
     - 三个以上大写字母连成的词：`XMLReader` → `XmlReader`，`ReadJSON` → `ReadJson`，`NPCSpawner` → `NpcSpawner`。
     - `ID`、`OK`：`UserID` → `UserId`。
   - 查不出 `Hp` 和 `HP` 哪个对：编译器不知道一个词是不是缩略词。文档和样例按规则写 `HP`。
   - 重写的成员、实现接口的成员不查，名字跟着基类。调用 .NET API 时照旧原样写（ADR-0006）。

4. **插值的 `{ }` 在开始的那一行闭合**，`$"..."` 和 `$"""..."""` 都一样。长的表达式先算出来：

   ```nyxel
   let alive = self.enemies.Where(func(e) = not e.IsDead).Count()
   Log.Info($"附近有 {alive} 个活着的敌人")
   ```

   - 按 C# 11 的习惯写了跨行的洞、并且它在后面某一行闭合时，报一个错（NYX1234），然后照 C# 读完这个洞，后面的代码照常分析。
   - 洞到行尾还没闭合、后面也找不到闭合它的 `}` 时，照旧报 NYX1011，在行尾补上 `}`。
   - 多行原始字符串的下一行本来就是字符串的内容，洞到行尾没闭合时照旧报 NYX1019（“在同一行闭合”），字符串接着读下一行。

## 备选方案

- 参数、返回值、异常：
  - **Javadoc / JSDoc / KDoc 的 `@param`、`@returns`、`@throws`**：Agent 见得最多，但不是 Markdown。渲染时相邻的几行合成一段，显示出来是一整行“@param text … @returns …”，源码和显示对不上。`@` 在 Nyxel 别处也不用。
  - **Rust 的标题 `# Parameters`、`# Errors`**：适合长篇说明，一两句的参数说明要占好几行。Rust 自己也很少逐个写参数。
  - **C# 的 XML 标签**：ADR-0007 已经不选。
  - **同时允许 Swift 的分组写法 `- Parameters:`**：参数多时整齐一些，但同一件事有两种写法，Agent 要选。
  - **`- Throws:` 只写说明**（同 Swift）：生成不了 `<exception cref>`，C# 那边看不到异常的类型。
  - **泛型参数另起一个词**（比如 `- TypeParameter T:`）：多记一个词，看名字已经分得开。
- 链接：
  - **只用方括号 `[TileMap.Parse]`**（Kotlin）：和正文里普通的方括号分不开。样例 TileMap 的文档里就写着“索引器：map[x, y]”，`list[i]` 也会被当成链接到 `i`。
  - **不加写法，能对上名字的代码片段自动当链接**：看不出哪个是链接，名字写错了也只是变成普通的代码片段，没有警告。
  - **同一个类型的成员直接写名字 `` [`Parse`] ``**：和代码里的规则不一致，代码里不带接收者的名字只能是局部变量或参数。
- 缩略词：
  - **当成普通单词 `PlayerUi`、`MaxHp`、`UserId`**（Rust、Google 的 Java 规范）：唯一能全部机械检查的写法（除了接口前缀 `I`，不许两个大写字母挨着），但会出现 `SaveIo` 挨着 .NET 的 `IOException`。
  - **一律全大写 `XMLReader`、`NPCSpawner`**（Swift、Go）：会出现 `JSONLoader` 挨着 .NET 的 `JsonSerializer`。
  - **不规定**：Agent 输出的风格会漂移，同 ADR-0006 不选“不规定命名”的理由。
- 插值：
  - **同 C# 11，洞里可以换行**：和 C# 一致。但字符串的文字被代码拆到几行上，要先找到 `}` 才知道文字从哪接着。没闭合的 `{` 会把后面几行代码都吞进洞里，直到碰到某个 `}`：Roslyn 对 `$"a {x b";` 后面再跟两行正常的代码报了 6 个错，其中有“`}` 要写成 `}}`”这种和真正问题无关的消息。现在的规则到行尾就报错，下一行照常分析。洞里换行在 C# 里本来就少见；现在不允许、以后放开，不会让已有的代码失效，反过来不行。

## 后果

- 前几轮的待定改成“后来定了”：ADR-0007 决策 1（参数和返回值的写法）、ADR-0014 后果（会抛哪些异常）、ADR-0020 后果（插值的 `{ }` 里换行）。ADR-0006 的命名规则补了缩略词。
- 文档注释的转换、链接的解析、第 1、2 项的警告都要先解析名字，在 M2 的 Binder 之后实现；XML 文档文件随 M3 的“C# 调用 Nyxel 编出的程序集”输出。缩略词的检查随命名检查（M2）实现。编译器现在还没有警告，这几项是最早的一批。
- 第 4 项落在 Lexer：
  - 洞到行尾没闭合时，往后看：把后面几行当代码读，括号配对，跳过字符串和注释；碰到同一层的 `}`、并且这一行后面有字符串的结尾引号时，算作跨行的洞，报 NYX1234，洞里的换行当普通的换行 trivia，Parser 照常按括号里的规则读。
  - 其余情况（先碰到没闭合的字符串、多出来的 `)`，或者 `}` 后面没有结尾引号）照旧报 NYX1011。
  - 新错误码 NYX1234。
- 样例：TileMap、Health、Collections 写了参数、返回值、异常和链接。language-reference 和各 ADR 例子里的 `Hp`、`maxHp` 改成 `HP`、`maxHP`：只是例子跟着新约定改名，不算修订。
