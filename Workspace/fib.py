import argparse


def fib(n):
    """迭代计算第 n 个斐波那契数
    
    时间复杂度: O(n)
    空间复杂度: O(1)
    """
    if n <= 1:
        return n
    a, b = 0, 1
    for _ in range(2, n + 1):
        a, b = b, a + b
    return b


def main():
    parser = argparse.ArgumentParser(description='计算第 n 个斐波那契数')
    parser.add_argument('n', type=int, help='斐波那契数列的索引 n')
    args = parser.parse_args()
    
    if args.n < 0:
        print("错误：请输入非负整数")
        return
    
    result = fib(args.n)
    print(f"F({args.n}) = {result}")


if __name__ == '__main__':
    main()
