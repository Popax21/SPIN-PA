*The name "SPIN theory" is derived from the fact that this theory can be visualized as a recursive set of sliding windows "spinning" / looping around in a residual number field*

## Introduction
Celeste's spinners can "load" and "unload" (= turn collision on or off by setting `Collidable`) only on certain frames where `Scene.OnInterval(float interval, float offset)` returns `true`. `interval` is always 0.05f for loading and 0.25f for unloading, while offset is a random float in `[0;1)` chosen when the room is loaded. The following code is a decompilation of `Scene.OnInterval`: 
```cs
public bool OnInterval(float interval, float offset) {
	return Math.Floor((TimeActive - offset - Engine.DeltaTime) / interval) < Math.Floor((TimeActive - offset) / interval);
}
```

`Scene.OnInterval` is intended to only return `true` for only one frame every `interval` seconds. As such the game uses this additional requirement to limit the number of collidable checks happening every frame.

This behavior leads to spinners being grouped into so called "spinner groups": the number of spinner groups is determined through the following formula: $$
n_g = \left \lfloor {\mathit{interval} \over \mathit{dt}} \right \rceil = \left \lfloor \mathit{interval}*{60} \right \rceil
$$
Note that the group count can be chosen arbitrarly as either $\left \lfloor {\mathit{interval} \over \mathit{dt}} \right \rfloor$ or $\left \lceil {\mathit{interval} \over \mathit{dt}} \right \rceil$ (except for ${\mathit{interval} \over \mathit{dt}} \in (1;2)$, in which case one has to round upwards), though the bigger $\lvert \mathit{interval} - n_g * \mathit{dt} \rvert = \lvert r \rvert$ is, where $r = \mathit{interval} - n_g * \mathit{dt}$ is called the *residual drift* of the groups, the less the spinners will follow the group pattern consistently over time.

Spinners will check whether they should load on every frame $t = i*n_g + g$ for some integer $i$, where $g \in [0;n_g)$ is called the spinner's *group (index)*. A "group cycle" consists of $n_g$ frames, where every spinner checks if it should load exactly once at some frame offset corresponding to its group number $g$. The following formula can be used to determine a spinner's group from its offset (the later analysis will contain a proof of a more general version of this statement): $$
g_{\mathit{off}} = \left \lceil {\mathit{off} \over \mathit{dt}} \right \rceil = \left \lceil {\mathit{off} * 60} \right \rceil \mod n_g
$$
*Note: the above equivalence $\left \lceil {\mathit{off} \over \mathit{dt}} \right \rceil = \left \lceil {\mathit{off} * 60} \right \rceil$ assumes that $\mathit{dt}$ is exactly $1 \over 60$-th of a second. The actual $\mathit{dt}$ obersved in game will be differemt vary over time, which will be explored later.

 For the scope of this document, frame indices are defined as the number of frames which have ellapsed since the scene (=`Level`) has become active.

As briefly mentioned above, spinners don't strictily adhere to this group pattern, can actually change which group they belong to at certain points in time - the rest of the document will be an analysis of why and when these spinner group changes occur.

## Why spinner offsets drift
**NOTE:** This analysis assumes that all operations are performed with perfect accuracy. While real-world floating point operations are subject to imprecision, for reasons beyond the scope of this document, the 32 bit floating point operations are actually executed with 80 bits of precision, which means that in practice floating point imprecision can be neglected for `OnInterval`.

Let $\mathit{dt}$ be the game's delta time (`Engine.DeltaTime` in the code), $T$ the time since the scene / level has been instantiated (`TimeActive` in the code), $\mathit{intv}$ and $\mathit{off}$ the `interval` and `offset` parameters respectively, and $n_g$ be the number of spinner groups same as above (note that even though $\mathit{intv}$ is fixed for spinner load groups, this analysis will be a generic analysis for every possible $\mathit{intv}$ and $\mathit{off}$). Note that because $T$ is incremented by $\mathit{dt}$, we can set $T = t*\mathit{dt}$, where $t$ is the current frame index.

"Translating" the C# code into mathmatical notation yields $$
\left \lfloor {T - \mathit{off} - \mathit{dt} \over \mathit{intv}} \right \rfloor < \left \lfloor {T - \mathit{off} \over \mathit{intv}} \right \rfloor
$$which is equivalent to $$
{T - \mathit{off} - \mathit{dt} \over \mathit{intv}} < \left \lfloor {T - \mathit{off} \over \mathit{intv}} \right \rfloor
$$
By multiplying both sides by $\mathit{intv}$ and substituting $\left \lfloor {a \over b} \right \rfloor * b = a - (a \bmod b)$ we can reformulate this to obtain: $$
T - \mathit{off} - \mathit{dt} < T - \mathit{off} - ((T - \mathit{off}) \bmod \mathit{intv})
$$
Subtracting $T - \mathit{off} - \mathit{dt}$ from both sides yields $$
-\mathit{dt} < -((T - \mathit{off}) \bmod \mathit{intv}) \iff (T - \mathit{off}) \bmod \mathit{intv} < \mathit{dt}
$$and because $\mathit{intv} > \mathit{dt} \iff \mathit{dt} \bmod \mathit{intv} = \mathit{dt}$ we get
$$
T - \mathit{off} = t*\mathit{dt} - \mathit{off} < \mathit{dt} \mod \mathit{intv}
$$
For purposes which will become apparent later, let us relax our assumptions slightly from our implict $\mathit{dt} > 0$ to $\lvert \mathit{dt} \rvert > 0$. This will allow $\mathit{dt}$ to be negative, which essentially means that time is flowing "backwards" through the cycle, and as such we are stepping through the cycle backwards. Additionally, we will decouple the right hand side of the condition from $\lvert \mathit{dt} \rvert$ by instead introducing a new symbol $\mathit{thr}$, which in this case equals $\mathit{thr} = \lvert \mathit{dt} \rvert$. The relaxed statement we will continue to further analyse is: $$
t*\mathit{dt} - \mathit{off} < \mathit{thr} \mod \mathit{intv}
$$
Now, because of spinner groups, we expect this expression to evaluate to the same value for every $t = c*n_g + i$, where $c$ is the index of the group cycle and $i$ is the offset into the cycle. Inserting this value of $t$ into the expression yields: $$
t*\mathit{dt} - \mathit{off} = (c*n_g + i)*\mathit{dt} - \mathit{off} = c*n_g*\mathit{dt} + i*\mathit{dt} - \mathit{off} < \mathit{thr} \mod \mathit{intv}
$$
Let us now define the *residual drift* $r$ as $r = \mathit{intv} - \lvert \mathit{dt} \rvert * n_g$ just like we did earlier. Inserting this new symbol into our condition we get: $$
{}{} \DeclareMathOperator{\sgn}{sgn}
\begin{aligned}
c*n_g*\mathit{dt} + i*\mathit{dt} - \mathit{off} < \mathit{thr} \mod \mathit{intv} \\
\iff c * \sgn \mathit{dt} * (\mathit{intv} - r) + i*\mathit{dt} - \mathit{off} < \mathit{thr} \mod \mathit{intv} \\
\iff i*\mathit{dt} - \mathit{off} - \sgn \mathit{dt} * r*c < \mathit{thr} \mod \mathit{intv} \\
\iff \mathbf{ i*\mathit{dt} - (\mathit{off} + \sgn \mathit{dt}* r*c) < \mathit{thr} \mod \mathit{intv} }
\end{aligned}
$$
Note that the final expression differs from what we would expect if all spinner cycles were exactly equal! Instead of $i*\mathit{dt} - \mathit{off} < \mathit{thr} \mod \mathit{intv}$, which would mean that the values of our expression would loop every group cycle (as it is only dependent on the group offset $i$), we got $i*\mathit{dt} - (\mathit{off} + \sgn \mathit{dt} * r*c) < \mathit{thr} \mod \mathit{intv}$. **This means that the effective offset of each spinner shifts by $\sgn \mathit{dt} * r$  every $n_g$ frames!**

## When group changes happen
A spinner will change its group as soon as $i*\mathit{dt} - (\mathit{off} + \sgn \mathit{dt} * r*c) < \mathit{thr} \mod \mathit{intv}$ no longer evaluates to the same value as $i*\mathit{dt} - \mathit{off} < \mathit{thr} \mod \mathit{intv}$. For convience, let $r' = \sgn \mathit{dt} * r$ and $\mathit{off}_c = \mathit{off} + r'*c$, $\mathit{}$. The two expressions from above can now be written as $i*\mathit{dt} - \mathit{off} < \mathit{thr} \mod \mathit{intv}$ and $i*\mathit{dt} - \mathit{off_c} < \mathit{thr} \mod \mathit{intv}$.

Observe that the expression, $i*\mathit{dt} - \mathit{off} < \mathit{thr} \mod \mathit{intv}$ is only ever true if $i$ is the spinner's group index. Solving for $i$ yields $$
\left \lceil {\mathit{off} \over \lvert \mathit{dt} \rvert} \right \rceil \leq
\sgn \mathit{dt} * i <
\left \lceil {\mathit{off} \over \lvert \mathit{dt} \rvert} \right \rceil + \left \lceil {\mathit{thr} - (-\mathit{off} \bmod \lvert \mathit{dt] \rvert}) \over \lvert \mathit{dt} \rvert} \right \rceil \mod n_g
$$
**This means that spinner groups are not actually discrete values, but *ranges* of length $\left \lceil {\mathit{thr} - (-\mathit{off} \bmod \lvert \mathit{dt] \rvert}) \over \lvert \mathit{dt} \rvert} \right \rceil$ for which all frame offsets in the range will pass the check!** We will continue to call $g = \sgn \mathit{dt} * \left \lceil {\mathit{off} \over \lvert \mathit{dt} \rvert} \right \rceil$ the *group* of the spinner, however keep in mind that in practice, the actual check results can differ from what we expect using the regular definition of spinner groups.

Note that we will focus on the left-hand side of the expression for the rest of this analysis, namely $g' = \left \lceil {\mathit{off} \over \lvert \mathit{dt} \rvert} \right \rceil$. This definition is useful as it still allows for checking if group changes occur, but also has the following relation hold: $$
r' > 0 \iff \mathit{off}_{c-1} < \mathit{off}_c \iff g'_{c-1} \leq g'_c
$$
(note that $r > 0 \iff g_{c-1} \leq g_c$ holds for the original definition - **this means that group indices will change by $\sgn r$ after drifting**)

We can now differentiate two different cases depending on the sign of $r'$:

- When $r' < 0$, then $g'_{c-1} \geq g'_c$. As such we want to find all values of $c$ for which $g'_{c-1} > g'_c \iff \left \lceil {\mathit{off}_{c-1} \over \lvert \mathit{dt} \rvert} \right \rceil > \left \lceil {\mathit{off}_c \over \lvert \mathit{dt} \rvert} \right \rceil$ holds. Multiplying both sides by $-1$ we get: $$
\begin{aligned}
-\left \lceil {\mathit{off}_{c-1} \over \lvert \mathit{dt} \rvert} \right \rceil < -\left \lceil {\mathit{off}_c \over \lvert \mathit{dt} \rvert} \right \rceil \\
\iff \left \lfloor {-(\mathit{off} + c*r' - r') \over \lvert \mathit{dt} \rvert} \right \rfloor < \left \lfloor {-(\mathit{off} + c*r') \over \lvert \mathit{dt} \rvert} \right \rfloor \\
\iff \mathbf { \left \lfloor {c*(-r') - \mathit{off} - (-r')) \over \lvert \mathit{dt} \rvert} \right \rfloor < \left \lfloor {c*(-r') - \mathit{off} \over \lvert \mathit{dt} \rvert} \right \rfloor }
\end{aligned}
$$
- When $r' > 0$, then $g_{c-1} \leq g_c$. As such we want to find all values of $c$ for which $g_{c-1} < g_c \iff \left \lceil {\mathit{off}_{c-1} \over \lvert \mathit{dt} \rvert} \right \rceil < \left \lceil {\mathit{off}_c \over \lvert \mathit{dt} \rvert} \right \rceil \iff \left \lceil {\mathit{off}_{c-1} \over \lvert \mathit{dt} \rvert} \right \rceil < {\mathit{off}_c \over \lvert \mathit{dt} \rvert}$ holds. Multiplying both sides by $\lvert dt \rvert$ and substituting $\left \lceil {a \over b} \right \rceil * b = a + (-a \bmod b)$ we get: $$
\begin{aligned}
\mathit{off}_{c-1} + (-\mathit{off}_{c-1} \bmod \mathit \lvert {dt} \rvert) < \mathit{off}_c \\
\iff \mathit{off}_{c-1} + (-\mathit{off}_{c-1} \bmod \mathit \lvert {dt} \rvert) < \mathit{off}_{c-1} + r' \\
\iff -\mathit{off}_{c-1} \bmod \mathit \lvert {dt} \rvert < r'
\end{aligned}
$$ Because $0 < r' < \mathit{dt}$ (as $|r'| = |\mathit{intv} - n_g * \lvert \mathit{dt} \rvert| = |\mathit{intv} - \left \lfloor {\mathit{intv} \over \lvert \mathit{dt} \rvert} \right \rceil * \lvert \mathit{dt} \rvert| < \lvert \mathit{dt} \rvert$), it holds that $r' \bmod \lvert \mathit{dt} \rvert = r'$, and as such: $$
\begin{aligned}
\iff -\mathit{off}_{c-1} < r' \mod \lvert \mathit{dt} \rvert \\
\iff -(\mathit{off} + r'*c - r') < r' \mod \lvert \mathit{dt} \rvert \\
\iff \mathbf { c*(-r') -(\mathit{off} - r') < \lvert r'\rvert \mod \lvert \mathit{dt} } \rvert
\end{aligned}
$$
- When $r' = 0$, then there is no drift, and as such group changes will never occur

**Note how both non-trivial cases match the exact structure of our original statement derived from `OnInterval`!** This means that the problem is recursive, and the indices of spinner cycles when spinners change groups behave just like a recursive instance of the original problem with the following parameters: $$
\begin{aligned}
&\mathit{dt}_R = -r' = -\sgn \mathit{dt} * r \\
&t_R = c \\
&\mathit{intv}_R = \lvert \mathit{dt} \rvert \\
&\mathit{off}_R = \begin{cases}
	\mathit{off} & \text{for } r' < 0 \iff \sgn \mathit{dt} \neq \sgn r\\
	\mathit{off} - r' = \mathit{off} + \sgn \mathit{dt} * r & \text{for } r' > 0 \iff \sgn \mathit{dt} = \sgn r\\
\end{cases} \\
&\mathit{thr}_R = \lvert r' \rvert \\
&\text{(where } r = \mathit{intv} - \lvert \mathit{dt} \rvert * n_g \text{)}
\end{aligned}
$$
(note that because $\mathit{thr}_R = \lvert \mathit{dt}_R \rvert$, recursive cycles are not affected by length drift - their group range will always have a length of 1)

All of this implies that the cycle indices $c$ during which spinners change groups are also cyclic, with spinners changing groups ever group cycle index $c$ where $c = j*{n_g}_R + g_R$, where ${n_g}_R = \left \lfloor {\mathit{intv}_R \over \lvert \mathit{dt}_R \rvert} \right \rceil$ is the period of these recursive "group drift cycles", and $g_R$ is the recursive "group drift cycle group". It initially starts as $g_R = \sgn \mathit{dt}_R * \left \lceil {\mathit{off}_R \over \lvert \mathit{dt}_R \rvert} \right \rceil \bmod {n_g}_R$, **but is then also recursively affected by group drifting!** Because of $r > 0 \iff g_{c-1} \leq g_c$ , **drifts decrement the group index** (= load checks happen one frame earlier than expected) **when $r > 0$, and increment it** (= load checks happen one frame later than expected) **when $r < 0$**

## The length of the check range
As we derived above, the actual group of a spinner is not a discrete value, but instead a range of values, namely: $$
\begin{aligned}
\left \lceil {\mathit{off} \over \lvert \mathit{dt} \rvert} \right \rceil \leq
\sgn \mathit{dt} * i <
\left \lceil {\mathit{off} \over \lvert \mathit{dt} \rvert} \right \rceil + \left \lceil {\mathit{thr} - (-\mathit{off} \bmod \lvert \mathit{dt] \rvert}) \over \lvert \mathit{dt} \rvert} \right \rceil \mod n_g \\
\iff \begin{cases}
	i \in \left[g; g + L\right) &\text{for } \mathit{dt} > 0 \\
	i \in \left(g - L; g\right] &\text{for } \mathit{dt} < 0
\end{cases} \\
\text{(where } L = \left \lceil {\mathit{thr} - (-\mathit{off} \bmod \lvert \mathit{dt} \rvert) \over \lvert \mathit{dt} \rvert} \right \rceil \text{)}
\end{aligned}
$$
(from now on, we will consider $L$ over $\mathit{off}_c$ instead of $\mathit{off}$)

$L$ is the length of this range, and by setting $\mathit{thr} = k * \lvert \mathit{dt} \rvert + o$, where $k = \left \lfloor {\mathit{thr} \over \lvert \mathit{dt} \rvert} \right \rfloor$ and $o = \mathit{thr} \bmod \lvert \mathit{dt} \rvert$, we can reformulate $L$ as $$
L =
\left \lceil {k * \lvert \mathit{dt} \rvert + o - (-\mathit{off}_c \bmod \lvert \mathit{dt} \rvert) \over \lvert \mathit{dt} \rvert} \right \rceil =
k + \left \lceil {o - (-\mathit{off}_c \bmod \lvert \mathit{dt} \rvert) \over \lvert \mathit{dt} \rvert} \right \rceil = k + l
$$
Because of $0 \leq o < \lvert \mathit{dt} \rvert$, $l$ can only ever be $0$ or $1$, as $-\lvert dt \rvert < o - (-\mathit{off} \bmod \lvert \mathit{dt} \rvert) < \lvert dt \rvert$. $l$ is $1$ (which is called a *length drift*) only if $$
\begin{aligned}
o - (-\mathit{off}_c \bmod \lvert \mathit{dt} \rvert) > 0 \\
\iff o > (-\mathit{off}_c \bmod \lvert \mathit{dt} \rvert) \\
\iff -\mathit{off}_c < o \mod \lvert \mathit{dt} \rvert \\
\iff -(\mathit{off} + r'*c) < o \mod \lvert \mathit{dt} \rvert \\
\iff \mathbf{ c*(-r') - \mathit{off} < o \mod \lvert \mathit{dt} \rvert } \\
\end{aligned}
$$
This defines another recursive cycle with $$
\begin{aligned}
&\mathit{dt}_L = -r' \\
&t_L = c \\
&\mathit{intv}_L = \lvert \mathit{dt} \rvert \\
&\mathit{off}_L = \mathit{off} \\
&\mathit{thr}_L = o = \mathit{thr} \bmod \lvert \mathit{dt} \rvert
\end{aligned}
$$
**Note that this almost exactly matches the definition of the regular recursive cycle!** The only two differences are:

- $\lvert r' \rvert = \mathit{thr}_R \neq \mathit{thr}_L = o$
	- This difference does not affect the group indicies $g_R = g_L$, however a further recursive cycle is required to predict the recursive length drift of $L_L$.
- when $r' > 0$: $\mathit{off} - r' = \mathit{off}_R \neq \mathit{off}_L = \mathit{off}$
	- In this case, the length drift cycle is one tick behind of the recursive cycle. The regular recursive cycle can still be used to predict the length drift, but one must look at the values from the previous tick instead of the current value. The recursive length drift cycle of $L_L$ must then also be adjusted to use $\mathit{off}_R$ instead of $\mathit{off}_L$, as this cancels out the fact that values are taken from the previous tick.


## Why this even happens
Careful readers might have noticed that all of the above analysis implicitly assumed that $r = \mathit{intv} \bmod \mathit{dt} \neq 0$, meaning that spinner offsets drift at all. However, as $\mathit{dt} = {1 \over 60}$ and $\mathit{intv} = 0.05$, $r$ should be $r = 0.05 \bmod {1 \over 60} = 0$. Then why is the above analysis relevant at all?

Simply put, in practice, the imprecisions of floating point numbers mean that neither $\mathit{dt}$ nor $\mathit{intv}$ are their ideal, non-drifting values. The following effects cause them to be ever so slightly different:

- $0.05$ is not exactly representable as a floating point number - the values used by the game's calculations is actually $0.0500000007450580596923828125$ (this also affects $\mathit{dt}$)
- `TimeActive` is incremented every frame by $0.0166667$-ish. However, as it only is a 32 bit float, depending on the current magnitude of `TimeActive`, the actual *effective* $\mathit{dt}$ will vary slightly because of float cancellation, and become less and less precise as time goes on. This results in "ranges" of different effective $\mathit{dt}$ values, which will be examined later.
    - Note that $\mathit{thr}$ is unaffected this effective deltatime imprecision - it will always remain the at the same precision and always equal the closest float value of $0.0166667$

## The effective deltatime ranges
`TimeActive` is stored in a 32 bit IEEE754 normalized floating point number. As such, it is comprised of a 23 bit mantissa `m = 1.XXXXXXXXXXXXXXXXXXXXXXX` (in binary), an 8 bit exponent `e = XXXXXXXX` and a sign bit `s`, which will always be zero. The value of such a float is given by $m * 2^{e - 127}$.

When adding two floating point numbers (like `TimeActive` and `DeltaTime`) with different exponents, a phenomenon called "float cancellation" occurs. This means that the lowest bits of the mantissa, which can now no longer be stored in the result are cut off. Additionally, the mantissa is rounded either up or down, depending on the highest trimmed of bit.

All of this effectively causes the effective deltatime to loose precision as the exponent of `TimeActive` gets bigger and bigger. Note that the effective deltatime is the same between two values of `TimeActive` with the same exponent, which allows for entire ranges of `TimeActive` values to be assigned the same effective deltatime. One edge case has to be taken into account though, which is when the next `TimeActive` value is already in the next range. In this case, the effective deltatime will be unique in some cases for one frame.

Included in the appendix are tables with various bits of information regarding the effective deltatime ranges.

## Appendix: Some data
### Effective Deltatime - Frame Ranges
`TimeActive` Exponent | Start Frame | End Frame
---|---|---|---
121 | 00000000 | 00000001
122 | 00000001 | 00000003
123 | 00000003 | 00000007
124 | 00000007 | 00000014
125 | 00000014 | 00000029
126 | 00000029 | 00000059
127 | 00000059 | 00000119
128 | 00000119 | 00000240
129 | 00000240 | 00000479
130 | 00000479 | 00000960
131 | 00000960 | 00001920
132 | 00001920 | 00003840
133 | 00003840 | 00007679
134 | 00007679 | 00015361
135 | 00015361 | 00030724
136 | 00030724 | 00061452
137 | 00061452 | 00122683
138 | 00122683 | 00246044
139 | 00246044 | 00492768
140 | 00492768 | 00986216
141 | 00986216 | 01918283
142 | 01918283 | 04015435
143 | 04015435 | 08209739
144 | 08209739 | 16598346
145 | 16598346 | 24986954
146 | 24986954 | ...

### Effective Deltatime - First `TimeActive` value in range
`TimeActive` Exponent | First `TimeActive` value in range
---|---
121 | 0.016666699200868606567382812500
122 | 0.033333398401737213134765625000
123 | 0.066666796803474426269531250000
124 | 0.133333608508110046386718750000
125 | 0.250000476837158203125000000000
126 | 0.500001132488250732421875000000
127 | 1.000002503395080566406250000000
128 | 2.000001668930053710937500000000
129 | 4.016666412353515625000000000000
130 | 8.000053405761718750000000000000
131 | 16.016597747802734375000000000000
132 | 32.016353607177734375000000000000
133 | 64.015869140625000000000000000000
134 | 128.012878417968750000000000000000
135 | 256.014953613281250000000000000000
136 | 512.002441406250000000000000000000
137 | 1024.010742187500000000000000000000
138 | 2048.015625000000000000000000000000
139 | 4096.000976562500000000000000000000
140 | 8192.004882812500000000000000000000
141 | 16384.013671875000000000000000000000
142 | 32768.003906250000000000000000000000
143 | 65536.007812500000000000000000000000
144 | 131072.015625000000000000000000000000
145 | 262144.000000000000000000000000000000
146 | 524288.000000000000000000000000000000

### Effective Deltatime - Effective $\mathit{dt}$ values
`TimeActive` Exponent | Effective $\mathit{dt}$ value
---|---
121 | -
122 | 0.0166666992008686065673828125
123 | 0.0166666954755783081054687500
124 | 0.0166666954755783081054687500
125 | 0.0166667103767395019531250000
126 | 0.0166667103767395019531250000
127 | 0.0166666507720947265625000000
128 | 0.0166666507720947265625000000
129 | 0.0166668891906738281250000000
130 | 0.0166664123535156250000000000
131 | 0.0166664123535156250000000000
132 | 0.0166664123535156250000000000
133 | 0.0166702270507812500000000000
134 | 0.0166625976562500000000000000
135 | 0.0166625976562500000000000000
136 | 0.0166625976562500000000000000
137 | 0.0167236328125000000000000000
138 | 0.0166015625000000000000000000
139 | 0.0166015625000000000000000000
140 | 0.0166015625000000000000000000
141 | 0.0175781250000000000000000000
142 | 0.0156250000000000000000000000
143 | 0.0156250000000000000000000000
144 | 0.0156250000000000000000000000
145 | 0.0312500000000000000000000000
146 | 0.0000000000000000000000000000

### Effective Deltatime - Transition $\mathit{dt}$ values
`TimeActive` Exponent | Transition $\mathit{dt}$ value
---|---
121 | 0.0166666992008686065673828125000000000000
122 | 0.0166666992008686065673828125000000000000
123 | 0.0166666954755783081054687500000000000000
124 | 0.0166666954755783081054687500000000000000
125 | 0.0166666805744171142578125000000000000000
126 | 0.0166667103767395019531250000000000000000
127 | 0.0166666507720947265625000000000000000000
128 | 0.0166668891906738281250000000000000000000
129 | 0.0166664123535156250000000000000000000000
130 | 0.0166673660278320312500000000000000000000
131 | 0.0166664123535156250000000000000000000000
132 | 0.0166702270507812500000000000000000000000
133 | 0.0166625976562500000000000000000000000000
134 | 0.0166778564453125000000000000000000000000
135 | 0.0166625976562500000000000000000000000000
136 | 0.0166625976562500000000000000000000000000
137 | 0.0166015625000000000000000000000000000000
138 | 0.0168457031250000000000000000000000000000
139 | 0.0166015625000000000000000000000000000000
140 | 0.0175781250000000000000000000000000000000
141 | 0.0175781250000000000000000000000000000000
142 | 0.0195312500000000000000000000000000000000
143 | 0.0234375000000000000000000000000000000000
144 | 0.0156250000000000000000000000000000000000
145 | 0.0312500000000000000000000000000000000000
146 | -

### Recursive cycle periods

Note: cycle 0 is the spinner group cycle (which always has a length of 3 for all exponents other than 145), and cycle 1 is the first recursive group change cycle.

`TimeActive` Exponent | C0 | C1 | C2 | C3 | C4 | C5 | C6 | C7 | C8
---|---|---|---|---|---|---|---|---|---
121 | 0 |      0 |      0 |       0 |     0 | 0 | 0 | 0 | 0
122 | 3 | 172074 |      3 |       9 |     0 | 0 | 0 | 0 | 0
123 | 3 | 194519 |      5 |       2 |     2 | 0 | 0 | 0 | 0
124 | 3 | 194519 |      5 |       2 |     2 | 0 | 0 | 0 | 0
125 | 3 | 127827 |      4 |       9 |     0 | 0 | 0 | 0 | 0
126 | 3 | 127827 |      4 |       9 |     0 | 0 | 0 | 0 | 0
127 | 3 | 344148 |      3 |       4 |     0 | 0 | 0 | 0 | 0
128 | 3 | 344148 |      3 |       4 |     0 | 0 | 0 | 0 | 0
129 | 3 |  24994 |      3 |      12 |     2 | 2 | 0 | 0 | 0
130 | 3 |  21824 |      3 |       5 |    13 | 0 | 0 | 0 | 0
131 | 3 |  21824 |      3 |       5 |    13 | 0 | 0 | 0 | 0
132 | 3 |  21824 |      3 |       5 |    13 | 0 | 0 | 0 | 0
133 | 3 |   1561 |      6 |       3 |    10 | 4 | 2 | 2 | 0
134 | 3 |   1365 |     12 |     273 |     0 | 0 | 0 | 0 | 0
135 | 3 |   1365 |     12 |     273 |     0 | 0 | 0 | 0 | 0
136 | 3 |   1365 |     12 |     273 |     0 | 0 | 0 | 0 | 0
137 | 3 |     98 |      7 |      48 |     3 | 4 | 6 | 2 | 0
138 | 3 |     85 |   3084 |      17 |     0 | 0 | 0 | 0 | 0
139 | 3 |     85 |   3084 |      17 |     0 | 0 | 0 | 0 | 0
140 | 3 |     85 |   3084 |      17 |     0 | 0 | 0 | 0 | 0
141 | 3 |      6 |      2 |       3 | 11651 | 2 | 4 | 0 | 0
142 | 3 |      5 | 838861 |       0 |     0 | 0 | 0 | 0 | 0
143 | 3 |      5 | 838861 |       0 |     0 | 0 | 0 | 0 | 0
144 | 3 |      5 | 838861 |       0 |     0 | 0 | 0 | 0 | 0
145 | 2 |      3 |      2 | 1677721 |     0 | 0 | 0 | 0 | 0
146 | 0 |      0 |      0 |       0 |     0 | 0 | 0 | 0 | 0

## How this can be used to efficently predict spinner group changes
**TODO**