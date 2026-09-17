/* Loads the cross-compiled ProbeLib.dll and calls its export, so the self-test
 * checks that the DLL runs rather than merely that it linked. */
#include <windows.h>
#include <stdio.h>

typedef int (*probe_t)(int);

int main(void)
{
    HMODULE h = LoadLibraryA("ProbeLib.dll");
    if (!h) {
        printf("FAIL: LoadLibraryA error %lu\n", (unsigned long)GetLastError());
        return 1;
    }

    probe_t probe = (probe_t)GetProcAddress(h, "ProbeExport");
    if (!probe) {
        printf("FAIL: GetProcAddress error %lu\n", (unsigned long)GetLastError());
        return 2;
    }

    int result = probe(41);
    printf("ProbeExport(41) = %d\n", result);
    return result == 42 ? 0 : 3;
}
