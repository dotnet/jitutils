#include <stdlib.h>
#include <stdio.h>

// This is a really long comment that needs to be reflowed. Clang-format should take care of this for us. This function
// is just a test for the code below.
static void testFunction(int* ptr)
{
    if (ptr)
    {
        printf("You passed a pointer!\n");
    }
    else
    {
        printf("You passed a nullptr!\n");
    }
}

int main(void)
{
    int i;

    // clang-tidy should insert braces in the below for loop. clang-format should removed the spaces above, and move the
    // braces clang-tidy adds below to the following line and re-indent them. Clang-formatting should fix all of the
    // indentation in this function.
    for (i = 0; i < 3; i++)
    {
        printf("Hello World!\n");
    }

    // clang-tidy should replace both of these instances with nullptr.
    testFunction(nullptr);
    testFunction(nullptr);

    return 0;
}
