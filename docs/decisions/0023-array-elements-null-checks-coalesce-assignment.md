# ADR-0023: 数组元素的初始值、和 null 比较只看引用、`??=`

- 状态：已接受
- 日期：2026-10-06

## 背景

第十八轮讨论，“待定”的第三轮：空值一节留下的三项（ADR-0009）。

- `new array<Enemy>(16)` 的元素一开始是 null，类型却说元素一定有值。C# 也有这个漏洞。
- 引擎可能重载 `==`：Unity 让已销毁的对象 `== null` 为 true，但 `?.`、`??`、`is null` 不走重载，同一个对象几种写法答案不同。
- C# 的 `??=` 要不要。

三项都和用户对比了代码，按推荐定。

## 决策

1. **只有“全零就是合法值”的元素类型能直接 `new array<T>(n)`**。.NET 新建数组的元素都是全零，所以：
   - 可以直接创建的：可空类型（`array<Enemy?>`）、数字、`bool`、`char`、不带数据的 enum、C# 那边定义的 struct（`Vector3`、`RaycastHit`，全零就是它们在 C# 里的默认值）。
   - 其余的（class、接口、带数据的 enum、Nyxel 自己的 struct）要给出每个元素：第二个参数是按下标创建元素的函数，名字是 `element`，编译成一个循环。
     ```nyxel
     let slots = new array<Slot>(16, func(i) = new Slot(index = i))
     let ps = new array<Particle>(length = 100, element = func(i) = new Particle())
     let grid = new array2d<Cell>(w, h, func(x, y) = new Cell(x = x, y = y))
     ```
   - Nyxel 的 struct 也算在内：它一定有 `init` 或字段初始值（ADR-0013），全零的元素绕过了它们。ADR-0013 不让对声明了 `init` 的 struct 写 `new Health()`，是同一个理由。
   - 让引擎往数组里填的写法（Unity 的 NonAlloc 系列），元素类型写成可空，取出来时处理 null：
     ```nyxel
     let hits = new array<Collider?>(32)
     let count = Physics.OverlapSphereNonAlloc(pos, radius, hits)
     for i in 0..<count {
         let hit = hits[i] ?? continue
         hit.SendMessage("Explode")
     }
     ```
2. **和 `null` 比较只看引用是不是空的**。
   - `x == null`、`x != null` 翻译成 C# 的 `x is null`、`x is not null`，不调用类型重载的 `==`。`?.`、`??`、`case null`、空值收窄本来就只看引用。所以 Nyxel 里所有和 null 有关的写法意思一样。
   - 两个值之间的 `a == b` 照常调用重载，只有一边是字面量 `null` 时不调用。
   - “已销毁”“已失效”这类状态由引擎提供显式的属性或方法（`world.IsAlive(entity)`、`obj.IsDestroyed`），不借用 null。参考仓库 EnginePlayground 就是这样：实体是 `Entity` 结构体句柄，用 `world.IsAlive(e)` 查。
3. **`??=` 同 C#，是语句**：左边是 null 时才求右边的值并赋值，左边只求值一次。
   ```nyxel
   name ??= "无名"                          // 局部变量：之后 name 当作非空
   self.target ??= self.FindTarget()        // 字段：只赋值，不收窄
   ```
   - 赋值是语句（ADR-0011），`??=` 也是：不能写 `return self.cache ??= ...`。
   - 收窄规则不变（ADR-0009）：局部变量和参数在 `??=` 之后当作非空；字段不收窄，要用时照旧先存进局部变量。
   - 行尾的 `??=` 和别的赋值运算符一样接着下一行（ADR-0020）。

## 备选方案

- **数组：只管 class，struct 全零同 C#**。好处：struct 数组照旧一行创建。坏处：`struct Particle { var life: float = 1 }` 的数组元素 `life` 是 0，读的人会以为是 1。
- **数组：同 C#，都允许**。好处：最省事，NonAlloc 的写法不用多 `?? continue`。坏处：`array<Enemy>` 的元素其实可能是 null，空值检查有漏洞，读的人会被类型误导。Kotlin、Swift、Dart 都不允许这样创建。
- **`== null` 同 C#：走重载，`?.`、`??` 不走**。好处：和 C# 一一对应。坏处：继承了 Unity 的坑，同一个对象 `== null` 和 `?.` 的答案不同；`case null` 也和 `== null` 不一致。
- **`== null`、`?.`、`??`、`case null` 全都走重载**。好处：对 Unity 用户最顺手，“已销毁就是 null”。坏处：每个 `?.`、`??` 都要调用用户定义的函数，可能慢、可能有副作用；“是不是 null”要看具体类型，和编译器按“引用是不是空”做的空值检查对不上。
- **不要 `??=`，写 `if x == null { x = ... }`**。坏处：Agent 和 C# 程序员常写 `??=`，改成 `if` 要多三行。

## 后果

- 这一轮全是语义规则，Parser 只加了 `??=` 这个记号。数组元素类型能不能直接 `new`、`== null` 的翻译、`??=` 之后的收窄都在 M2 实现。
- .NET 的方法照样能产生 null 元素（`Array.Resize` 新增的格子、`Array.Clear`），编译器管不到；这和调用任何 .NET API 一样，按 C# 那边的标注处理（ADR-0009）。
- 留给后面几轮：
  - 第 4 轮：简单枚举如果能自定义底层值，全零可能不是任何一个 case，到时候要决定这种 enum 的数组能不能直接 `new`。
  - 第 6 轮：泛型参数 `T` 的数组能不能直接 `new`，和泛型的可空性一起定。
- 接入 Unity 那样重载了 `==` 的引擎时，绑定层要提供显式的“是否已销毁”。
