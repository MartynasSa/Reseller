import { Button } from '@/components/ui/button'

export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-4 p-8 text-center">
      <h1 className="text-4xl font-bold">Reseller</h1>
      <p className="text-muted-foreground">Marketplace alerts for resellers.</p>
      <Button>Get started</Button>
    </main>
  )
}
