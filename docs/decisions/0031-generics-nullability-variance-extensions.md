# ADR-0031: 泛型的 `T?` 和 `notnull`、元素是 T 的数组、不声明协变逆变、泛型扩展 `extension<T>`

- 状态：已接受
- 日期：2026-10-30

## 背景

第二十四轮讨论，“待定”的第六轮第一批：泛型。前几轮留下四件事：

- 泛型的可空性：`T?` 在 T 是 class 和 struct 时含义不同（ADR-0012 待定）。
- `new array<T>(n)` 能不能直接创建（ADR-0023 留给第 6 轮）。
- 泛型接口的协变 / 逆变（ADR-0012 待定）。
- 泛型扩展 `extension<T> List<T>`（ADR-0014 待定）。

第 6 轮的另一半（`var self` 扩展、C# 里没标 `readonly` 的 struct 方法、临时副本的范围）是第二批，下一轮讨论。

查了 EnginePlayground：泛型全是 `where T : unmanaged` 的 ECS 组件（`World.Get<T>`、`Query.Read<T>`）；没有自己声明的协变接口，也没有泛型扩展。

用 Roslyn 5.9（C# 14，开启可空检查）验证过：

- 没约束的 `T? Find<T>(...)` 在 T 是 int 时返回 int，找不到时运行结果是 0；调用方写 `r is null` 是 CS0037。
- 没约束、只有 `notnull` 或只有接口约束的 `T?`，`return null` 是 CS0403（要写 `default`）；基类约束的 `T?` 可以 `return null`。
- 没约束的 T 的值调用成员：警告 CS8602。可空类型代入有接口约束的 T：警告 CS8631。没约束的 T 当 `Dictionary` 的键：警告 CS8714。
- 协变 / 逆变只能标在接口和委托上（CS1960），用法不对是 CS1961。值类型不参与：`List<int>` 不能当 `IEnumerable<object>`（CS0266）。
- C# 14 的 `extension<T>(List<T> list) where T : ...` 可用，也能扩展 `T` 本身和 `List<int>` 这种具体类型；extension 的类型参数在被扩展的类型里没出现是 CS9295。

四项都按推荐定。

## 决策

1. **写 `T?` 时，约束里要看得出 T 是 class 还是 struct。**

   ```nyxel
   static func Find<T: class>(items: List<T>, pred: func(T) -> bool) -> T? {
       for x in items {
           if pred(x) { return x }
       }
       return null
   }

   static func FindValue<T: struct>(items: List<T>, pred: func(T) -> bool) -> T? { ... }   // T? 是 Nullable<T>

   static func Find<T>(items: List<T>, ...) -> T?     // 错误：T 可能是 int，这时 T? 不表示“可能没有”
   ```

   - 能写 `T?` 的约束：`class`、基类（`T: Enemy`）是 class 那一种；`struct`、`unmanaged` 是 struct 那一种（`Nullable<T>`）。没约束、只有 `notnull` 或只有接口约束时写 `T?` 是编译错误，诊断建议加 `class` 或 `struct`。
   - 所以 Nyxel 里的 `T?` 永远表示“可能是 null”，`return null` 照常写，不需要 `default`。
   - 例外：实现或重写 .NET 成员时，签名里有没约束的 `T?`（`IComparer<T>.Compare(x: T?, y: T?)`）就照签名写，含义同 C#。这和 ADR-0015“`out` / `ref` 只在 .NET 签名要求时声明”是同一个思路。
   - 调用 .NET 里返回没约束 `T?` 的方法同 C#：`FirstOrDefault()`、`GetValueOrDefault()` 在 int 上返回 int（没有时是 0），在 `Enemy` 上返回 `Enemy?`。
   - 没约束的 T 可以代入可空类型（`List<Enemy?>`、`array<Collider?>`）。所以泛型代码里 T 的值可能是 null，调用它的成员前要先检查（C# 是警告 CS8602，Nyxel 按 ADR-0009 是错误）。
   - 新增约束 `notnull`（同 C#）：T 可以是任何不可空的类型，比如当 `Dictionary` 的键：`class Registry<K: notnull, V>`。有 `class`、`struct`、基类或接口约束的 T 本来就不能代入可空类型（C# 是警告 CS8631，Nyxel 是错误）。
   - 翻译：`T: class` 是 C# 的 `where T : class`，`T?` 是可空引用；`T: struct` 的 `T?` 是 `Nullable<T>`；`notnull` 同 C#。

2. **元素类型是 T 的数组要写 `element`；元素类型是 `T?` 的可以直接创建。**

   ```nyxel
   class RecentList<T: class> {
       let items: array<T?>
       init(capacity: int) {
           self.items = new array<T?>(capacity)                  // 直接创建，全是 null
       }
   }

   class Grid<T> {
       var cells: array<T>
       init(w: int, h: int, fill: func(int) -> T) {
           self.cells = new array<T>(w * h, element = fill)      // 每个格子由调用方给出
       }
   }

   self.items = new array<T>(16)     // 错误：T 可能是 Enemy，元素会是 null
   ```

   - 道理同 ADR-0023 决策 1：只有“全零就是合法值”的元素类型能直接创建。T 是类型参数时编译器不知道全零对它合不合法：没约束或接口约束的 T 可能是 `Enemy`；`T: struct`、`T: unmanaged` 也可能是有 `init` 的 Nyxel struct。
   - `T?`（有 `class` 或 `struct` 约束）的全零就是 null，可以直接创建。
   - 代价：Nyxel 里写不出“对任何 T 都能用、内部留空格子”的容器（`List<T>` 这种）。这种容器 .NET 都有现成的。

3. **暂不支持声明协变 / 逆变；用 .NET 已有的同 C#。**

   ```nyxel
   let all: IEnumerable<Enemy> = slimes            // 能：IEnumerable<out T> 是 .NET 标好的
   let logSlime: func(Slime) = logAnyEnemy         // 能：函数类型的参数逆变、返回值协变

   public interface ISource<out T> { ... }         // 错误（NYX1223）：写 ISource<T>
   ```

   - 协变：只往外出 T 的接口（`IEnumerable<T>`），`X<Slime>` 可以当 `X<Enemy>` 用；逆变：只往里进 T 的接口（`IComparer<T>`），`X<Enemy>` 可以当 `X<Slime>` 用；既进又出的（`List<T>`）两个方向都不行。
   - 用 .NET 已经标好的接口和 `func(...)` 函数类型（`Func` / `Action`），转换规则同 C#，包括值类型不参与。
   - Nyxel 自己声明的接口不能标 `in` / `out`。写了是编译错误（NYX1223），按没写读下去。要让 `ISpawner<Slime>` 传给需要刷怪点的函数，把函数写成泛型：`func Fill<T: Enemy>(spawner: ISpawner<T>)`。
   - 以后要加，写法同 C#：`interface ISource<out T>`。

4. **泛型扩展的类型参数写在 `extension` 后面。**

   ```nyxel
   extension<T> List<T> {
       public func Shuffle(self, rng: Random) { ... }
       public func Map<U>(self, convert: func(T) -> U) -> List<U> { ... }   // 只有这个方法用的 U 写在方法上
   }

   extension<T: IComparable<T>> List<T> {
       public func Largest(self) -> T { ... }
   }

   extension List<int> {                             // 具体类型不需要类型参数
       public func Total(self) -> int { ... }
   }

   extension<T: IComparable<T>> T {                  // 扩展所有可比较的类型：5.Clamp(lo = 0, hi = 3)
       public func Clamp(self, lo: T, hi: T) -> T { ... }
   }
   ```

   - `<T>` 先声明，后面的类型再用它，同 `class Pool<T: class>` 名字后面的 `<>`；约束照旧写在 `<>` 里。Rust 的 `impl<T> Trait for Vec<T>` 也是这个顺序。
   - `extension` 后面的每个类型参数都要在被扩展的类型里出现（NYX1224，C# 的 CS9295）。只有某些成员用的类型参数写在那个成员上。
   - 块里的规则同 ADR-0014 决策 3。
   - 翻译：C# 14 的 `extension<T>(List<T> self) where T : ...`，放在 `ListExtensions` 里。

## 备选方案

- `T?`：
  - **同 C#，没约束的 `T?` 表示“T 或默认值”，加 `default` 关键字**：Agent 熟，一个函数对什么 T 都能用。但签名写着 `T?`，T 是 int 时找不到却返回 0，和真的 0 分不出来，调用方也没法检查 null；还要多一个关键字。
- 元素是 T 的数组：
  - **`T: struct` / `unmanaged` 也能直接创建**（按 C# 的 struct 对待）：数字网格写起来短，但代入 Nyxel 的 struct 时绕过了它的 `init`，正是 ADR-0023 要挡的。
  - **同 C#，什么 T 都能直接创建**：最省事，但 T 是 `Enemy` 时 `array<T>` 里全是 null，空值检查有漏洞。
- 协变 / 逆变：
  - **同 C#，`interface ISource<out T>`**：Agent 熟，Kotlin 也这样写。但 `in` / `out` 已经是参数修饰（`data: out SaveData`、`value: in Matrix4x4`），`for x in` 里还有一个 `in`，一个词三种意思；游戏脚本很少自己声明这种接口，EnginePlayground 一个都没有，真碰到了可以用泛型函数绕过。
- 泛型扩展：
  - **`extension List<T>`，T 自动成为类型参数**（Swift 的 `extension Array` 类似）：短，但读到 `T` 不知道它是类型参数，还是别处定义了一个叫 `T` 的类型；约束也没地方写。

## 后果

- 没有修改旧决定。ADR-0012、0014、0023 留下的这几项待定由本 ADR 定了。
- `notnull` 和 `unmanaged` 一样按类型名读，语义分析时认出来，不是关键字。
- 语法树：`ExtensionDeclarationSyntax.TypeParameterList`。Parser 查只看语法就知道的：类型参数前的 `in` / `out`（NYX1223，报错后按没写读下去）、extension 的类型参数没出现在被扩展的类型里（NYX1224）。
- 留给语义分析（M2）：写 `T?` 的约束够不够、实现 .NET 成员时没约束 `T?` 的例外；没约束的 T 的值用成员前要检查；可空类型代入有约束的 T、没约束的 T 当 `notnull` 的类型参数；元素类型是 T 的数组能不能直接创建；`extension List<T>` 漏写 `<T>` 时，“找不到类型 T”的诊断给出 `extension<T> List<T>` 的写法。
- 样例 samples/Common/Collections.nyxel：泛型扩展、`T: class` 的 `T?` 和数组、`notnull`。
- 待定：声明协变 / 逆变（以后同 C# 的写法）。
