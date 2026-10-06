import { defineConfig } from 'vitest/config';

export default defineConfig({
  // Relative asset paths so the build works from any sub-path (e.g. GitHub Pages /<repo>/).
  base: './',
  test: {
    include: ['test/**/*.test.ts'],
  },
});
