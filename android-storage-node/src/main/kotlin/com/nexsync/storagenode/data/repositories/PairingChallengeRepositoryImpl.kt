package com.nexsync.storagenode.data.repositories

import com.nexsync.storagenode.data.db.daos.PairingChallengeDao
import com.nexsync.storagenode.data.db.entities.PairingChallengeEntity
import com.nexsync.storagenode.domain.entities.PairingChallenge
import com.nexsync.storagenode.domain.repositories.PairingChallengeRepository
import java.util.UUID
import javax.inject.Inject

class PairingChallengeRepositoryImpl @Inject constructor(
    private val dao: PairingChallengeDao
) : PairingChallengeRepository {

    override suspend fun savePairingChallenge(challenge: PairingChallenge) {
        val entity = PairingChallengeEntity(
            challengeId = challenge.challengeId.toString(),
            userId = challenge.userId.toString(),
            code = challenge.code,
            expiresAt = challenge.expiresAt,
            isUsed = challenge.isUsed,
            createdAt = challenge.createdAt,
            usedAt = null
        )
        dao.insert(entity)
    }

    override suspend fun getPairingChallenge(challengeId: String): PairingChallenge? {
        return dao.getByChallengeId(challengeId)?.toDomain()
    }

    override suspend fun getUnusedChallenge(code: String): PairingChallenge? {
        return dao.getUnusedByCode(code, System.currentTimeMillis())?.toDomain()
    }

    override suspend fun markChallengeAsUsed(challengeId: String) {
        dao.markAsUsed(challengeId, System.currentTimeMillis())
    }

    override suspend fun deleteExpiredChallenges() {
        dao.deleteExpired(System.currentTimeMillis())
    }

    private fun PairingChallengeEntity.toDomain(): PairingChallenge {
        return PairingChallenge(
            challengeId = UUID.fromString(this.challengeId),
            userId = UUID.fromString(this.userId),
            code = this.code,
            expiresAt = this.expiresAt,
            isUsed = this.isUsed,
            createdAt = this.createdAt
        )
    }
}
